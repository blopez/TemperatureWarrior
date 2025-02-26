// @ts-check

const PASS = "pass";

let ranges_changed = false;
let connected = false;
let round_started = false;
let round_updates_counter = 0;
let current_time = 0;
let refresh_rate = 0;
let global_ranges;
let total_time = 0;
let round_is_test = false;

/**
 * @param {WebSocket} webSocket
 * @returns {function(MessageEvent): any} message
 */
function onMessage(webSocket) {
    return async e => {
        const json = await e.data.text();
        const message = JSON.parse(json);
        console.log(message.type);

        switch (message.type) {
            case 'N':
                console.log("received N");
                if (!round_started) return;
                if (round_updates_counter == 0)
                    change_round_status('running');
                round_updates_counter += 1;
                array = message.ns
                for (const key in array) {
                    temp = array[key]
                    if (!isNaN(temp))
                        add_chart_point(current_time, temp);
                    current_time += refresh_rate
                }
                break;

            case 'TempTooHigh':
                stop_round();
                change_round_status('tshutdown');
                round_updates_counter = 0;

                if (round_is_test) {
                    document.getElementById("test-sensor").textContent = 'Test Sensor';
                    round_is_test = false;
                    change_round_status('unset');
                }

                break;

            case 'ShutdownCommand':
                stop_round();
                change_round_status('mshutdown');
                round_updates_counter = 0;

                if (round_is_test) {
                    document.getElementById("test-sensor").textContent = 'Test Sensor';
                    round_is_test = false;
                    change_round_status('unset');
                }

                break;

            case 'RoundFinished':
                for (const temp of message.ns) {
                    if (!isNaN(temp))
                        add_chart_point(current_time, temp);
                    current_time += refresh_rate
                }
                
                stop_round();
                if (round_is_test) {
                    document.getElementById("test-sensor").textContent = 'Test Sensor';
                    round_is_test = false; // para la próxima ronda
                    change_round_status('unset');
                } else {
                    const timeInSec = (message.timeInRange / 1000).toFixed(3);
                    change_round_status('finished', timeInSec);
                }

                round_updates_counter = 0;
                break;
            
            case 'Bad Format':
                console.log('mal formato');
                document.getElementById("send-round").disabled = true;
                break;

            case 'ConfigOK':
                if (round_is_test) {
                    set_test_chart();
                    webSocket.send(JSON.stringify({ type: "Start" }));
                    round_started = true;
                } else {
                    set_round_chart(global_ranges);
                    ranges_changed = false;
                    document.getElementById("start-round").disabled = false;
                    change_round_status('ready');
                }
                current_time = 0;
                break;
            default:
                console.warn(`Mensaje no reconocido: ${json}`);
                break;
        }
    };
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function sendRound(webSocket) {
    return e => {
        if (!webSocket.OPEN)
            throw new Error("Socket is closed");

        e.preventDefault();

        const ranges = get_ranges_values();
        if (!ranges) return;
        global_ranges = ranges;

        hide_range_errors();

        const rateElem = document.getElementById('rate');
        const refreshInMilliseconds = parseInt(rateElem?.value);
        if (isNaN(refreshInMilliseconds) || refreshInMilliseconds <= 0) {
            rateElem?.classList.add('error');
            return;
        }
        rateElem?.classList.remove('error');

        webSocket.send(JSON.stringify({
            type: "Command",
            data: { refreshInMilliseconds, pass: PASS, ranges, isTest: false }
        }));

        refresh_rate = refreshInMilliseconds / 1000;
        const sendBtn = document.getElementById("send-round");
        sendBtn.disabled = true;
        sendBtn.textContent = 'Cambiar Ronda';
        total_time = ranges.reduce((acc, range) => acc + range.roundTime, 0);
    };
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function startRound(webSocket) {
    return e => {
        e.preventDefault();
        if (!webSocket.OPEN)
            throw new Error("Socket is closed");

        webSocket.send(JSON.stringify({ type: "Start" }));
        round_started = true;
        const sendBtn = document.getElementById("send-round");
        sendBtn.disabled = true;
        sendBtn.textContent = 'Enviar Ronda';
        document.getElementById("start-round").disabled = true;
    };
}

function handleTest(webSocket) {
    return e => {
        e.preventDefault();
        if (!webSocket.OPEN)
            throw new Error("Socket is closed");
        
        
        if (!round_is_test) {
            round_is_test = true;
            document.getElementById("send-round").disabled = true;
            document.getElementById("test-sensor").textContent = 'Stop Test';
            
            webSocket.send(JSON.stringify({
                type: "Command",
                data: { 
                    refreshInMilliseconds: 1000,
                    pass: PASS,
                    isTest: true,
                    ranges: [{tempMax: 30, tempMin: 12, roundTime: 60 }],
                }
            }));
            total_time = 60;
            refresh_rate = 1;
        } else {
            webSocket.send(JSON.stringify({ type: "Shutdown" }));
            document.getElementById("test-sensor").textContent = 'Test Sensor';
        }
    };
}

function sendShutdown(webSocket) {
    return e => {
        e.preventDefault()

        if (!webSocket.OPEN)
            throw new Error("Socket is closed");

        webSocket.send(JSON.stringify({ type: "Shutdown" }));
    }
}

/**
 * @param {WebSocket} webSocket
 * @returns {function(Event): any}
 */
function onOpen(webSocket) {
    return _ => {
        connected = true;
        console.log("===== CONNECTION OPEN =====");
        document.querySelector('.server-settings .connect-field')?.classList.add('hide');
        document.querySelector('.server-settings .messages')?.classList.add('hide');
        document.querySelector('.server-settings .connection-success')?.classList.remove('hide');
    }
}

/**
 * @param {WebSocket} webSocket
 * @returns {function(CloseEvent): any}
 */
function onClose(webSocket) {
    return e => {
        connected = false;
        console.log("===== CONNECTION CLOSED =====");
        show_connect_error(e.reason || "Conexión cerrada");

        const sendBtn = document.getElementById("send-round");
        const startBtn = document.getElementById("start-round");

        sendBtn.disabled = true;
        startBtn.disabled = true;

        reset_element(sendBtn);
        reset_element(startBtn);

        change_round_status('unset');
        clear_graph();
    };
}

/**
 * @param {WebSocket} webSocket
 * @returns {EventListener}
 */
function onError(webSocket) {
    return e => {
        console.warn("===== CONNECTION ERROR =====");
        show_connect_error("Error en la conexsión");
    };
}

function main() {
    const connectBtn = /** @type {HTMLButtonElement} */ (document.getElementById("server-connect"));
    connectBtn.addEventListener("click", e => {
        e.preventDefault();
        const input = /** @type {HTMLInputElement} */ (document.getElementById("ip"));
        const ip = input.value;
        console.log(`IP Server: ${ip}`);
        try {
            const webSocket = new WebSocket(`ws://${ip}/`);

            webSocket.addEventListener("open", onOpen(webSocket));
            webSocket.addEventListener("close", onClose(webSocket));
            webSocket.addEventListener("error", onError(webSocket));
            webSocket.addEventListener("message", onMessage(webSocket));

            const sendBtn = document.getElementById("send-round");
            const startBtn = document.getElementById("start-round");
            const shutdownBtn = document.getElementById("shutdown");
            const testBtn = document.getElementById("test-sensor");

            sendBtn?.addEventListener("click", sendRound(webSocket));
            startBtn?.addEventListener("click", startRound(webSocket));
            shutdownBtn?.addEventListener("click", sendShutdown(webSocket));
            testBtn?.addEventListener("click", handleTest(webSocket));

            sendBtn.disabled = false;
            shutdownBtn.disabled = false;
            testBtn.disabled = false;
        } catch {
            show_connect_error("Ruta del servidor invalida");
        }

    });
}

document.addEventListener("DOMContentLoaded", main);