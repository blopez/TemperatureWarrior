// @ts-check

import http from 'http';
import fs from 'fs/promises'; // fs con interfaz de Promise (y async/await)
import path from 'path';
import url from 'url';


// Necesario para simular __filename y __dirname en un proyecto de
// nodejs que usa módulos ES6
/** @type {string} */
const __filename = url.fileURLToPath(import.meta.url);
/** @type {string} */
const __dirname = path.dirname(__filename);

/** @type {number} */
const PORT = 8080;

/** @typedef {"text/html" | "text/javascript" | "text/css" | "application/json"} ContentType
/** @typedef {Buffer | string} Content */
/** @typedef {"GET" | "POST" | "PATCH" | "PUT" | "DELETE"} HttpMethod */
/** @typedef {200|400|404|405|409|500} StatusCode */
/**
 * @typedef {Object} ResponseData
 * @property {StatusCode} status
 * @property {Content} content
 * @property {ContentType} contentType
 */

/**
 * @param {string} filename - Path relativo al directorio de este archivo
 * @returns {Promise<Content>}
 */
async function resolveFile(filename) {
	return await fs.readFile(path.resolve(__dirname, filename));
}

/**
 * @param {StatusCode} status
 * @param {Content} content
 * @param {ContentType} contentType
 * @returns {ResponseData}
 */
const makeResponseData = (status, content, contentType) => ({ status, content, contentType });

/**
 * @param {http.ServerResponse} res
 * @param {ResponseData} data
 */
function sendResponse(res, data) {
	res.writeHead(data.status, { "Content-Type": data.contentType });
	res.end(data.content);
}

/**
 * @param {http.IncomingMessage} req
 * @returns {Promise<Buffer>}
 */
function getRequestBody(req) {
	/** @type {Buffer[]} */
	let chunks = [];
	return new Promise((resolve, reject) => {
		req.on('data', chunk => chunks.push(chunk));
		req.on('end', () => resolve(Buffer.concat(chunks)));
		req.on('error', err => reject(err));
	});
}

/**
 * @param {string} filename
 * @returns {ContentType}
 */
function filenameToContentType(filename) {
	switch (path.extname(filename)) {
		case ".js":
			return "text/javascript";
		case ".html":
			return "text/html";
		case ".css":
			return "text/css";
		case ".json":
			return "application/json";
		default:
			throw new Error("Extensión inválida");
	}
}

/** @type {ResponseData} */
const INVALID_ROUTE_RESPONSE = makeResponseData(400, "Ruta invalida\r\n", "text/html");

/**
 * @param {http.IncomingMessage} req
 * @returns {Promise<ResponseData>}
 */
async function resolveRequest(req) {
	if (req.url === undefined)
		return INVALID_ROUTE_RESPONSE;

	switch (req.method) {
		case "GET":
			switch (req.url) {
				case "/":
					return makeResponseData(200, await resolveFile("www/index.html"), "text/html");
				default:
					try {
						return makeResponseData(200, await resolveFile(`www/${req.url}`), filenameToContentType(req.url));
					} catch (_) {
						return INVALID_ROUTE_RESPONSE;
					}
			}
		case "POST":
			switch (req.url) {
				default:
					return INVALID_ROUTE_RESPONSE;
			}
		case "DELETE":
			switch (req.url) {
				default:
					return INVALID_ROUTE_RESPONSE;
			}
		case "PATCH":
			switch (req.url) {
				default:
					return INVALID_ROUTE_RESPONSE;
			}
		default:
			return makeResponseData(405, `Método: "${req.method}" no permitido!\r\n`, "text/html");
	}
}

/**
 * @param {http.IncomingMessage} req
 * @param {http.ServerResponse} res
 */
async function routeRequest(req, res) {
	sendResponse(res, await resolveRequest(req));
}

http.createServer(routeRequest).listen(PORT);
console.log(`Server running at: 127.0.0.1:${PORT}`);
