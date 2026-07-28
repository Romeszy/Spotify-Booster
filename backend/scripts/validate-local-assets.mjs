import { chmodSync, closeSync, constants, openSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { createPrivateKey, createPublicKey, sign, verify, X509Certificate } from 'node:crypto';

const P256_ORDER = BigInt('0xffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551');
const exactKeys = ['alg','crv','d','ext','key_ops','kid','kty','use','x','y'].sort();

function fail(category) { throw new Error(category); }
function under(path, parent) {
  const p = resolve(path); const root = resolve(parent);
  if (p !== root && !p.startsWith(root + (process.platform === 'win32' ? '\\' : '/'))) fail('path_outside_local_root');
}
function base64url32(value, field) {
  if (typeof value !== 'string' || value.length === 0 || value.includes('=') || !/^[A-Za-z0-9_-]+$/.test(value)) fail(`invalid_${field}_encoding`);
  const decoded = Buffer.from(value, 'base64url');
  if (decoded.length < 1 || decoded.length > 32) fail(`invalid_${field}_length`);
  const normalized = Buffer.alloc(32); decoded.copy(normalized, 32 - decoded.length);
  return normalized.toString('base64url');
}
function normalizeJwk(document) {
  if (!Array.isArray(document)) fail('invalid_signing_document_shape');
  const list = document;
  if (list.length !== 1 || !list[0] || Array.isArray(list[0]) || typeof list[0] !== 'object') fail('invalid_signing_document_shape');
  const jwk = { ...list[0] };
  if (JSON.stringify(Object.keys(jwk).sort()) !== JSON.stringify(exactKeys)) fail('invalid_signing_fields');
  if (jwk.kty !== 'EC' || jwk.use !== 'sig' || jwk.alg !== 'ES256' || jwk.crv !== 'P-256' || jwk.ext !== true) fail('invalid_signing_algorithm');
  if (!Array.isArray(jwk.key_ops) || jwk.key_ops.length !== 2 || jwk.key_ops[0] !== 'sign' || jwk.key_ops[1] !== 'verify') fail('invalid_signing_operations');
  if (typeof jwk.kid !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(jwk.kid)) fail('invalid_signing_kid');
  jwk.x = base64url32(jwk.x, 'x'); jwk.y = base64url32(jwk.y, 'y'); jwk.d = base64url32(jwk.d, 'd');
  const d = BigInt('0x' + Buffer.from(jwk.d, 'base64url').toString('hex'));
  if (d <= 0n || d >= P256_ORDER) fail('invalid_private_scalar');
  let privateKey, publicKey;
  try {
    privateKey = createPrivateKey({ key: jwk, format: 'jwk' });
    publicKey = createPublicKey(privateKey);
    const message = Buffer.from('spotify-booster-local-signing-key-validation');
    const signature = sign('sha256', message, privateKey);
    if (!verify('sha256', message, publicKey, signature)) fail('signing_roundtrip_failed');
  } catch { fail('invalid_p256_key_material'); }
  return [jwk];
}
function enforceMode(path, expected) {
  if (process.platform !== 'win32') {
    chmodSync(path, expected);
    if ((statSync(path).mode & 0o777) !== expected) fail('invalid_posix_permissions');
  }
}
function validateTls(certPath, keyPath, root) {
  under(certPath, root); under(keyPath, root);
  const cert = new X509Certificate(readFileSync(certPath));
  const now = Date.now();
  if (now < Date.parse(cert.validFrom) || now >= Date.parse(cert.validTo)) fail('certificate_expired_or_not_yet_valid');
  if (!(cert.subjectAltName || '').split(',').map(x => x.trim()).includes('IP Address:127.0.0.1')) fail('certificate_missing_loopback_ip_san');
  let key;
  try { key = createPrivateKey(readFileSync(keyPath)); } catch { fail('invalid_tls_private_key'); }
  const certSpki = cert.publicKey.export({ type: 'spki', format: 'der' });
  const keySpki = createPublicKey(key).export({ type: 'spki', format: 'der' });
  if (!certSpki.equals(keySpki)) fail('tls_certificate_key_mismatch');
  enforceMode(keyPath, 0o600);
}
function validateJwks(signingPath, jwksPath, root) {
  under(signingPath, root); under(jwksPath, root);
  const [privateJwk] = normalizeJwk(JSON.parse(readFileSync(signingPath, 'utf8')));
  const document = JSON.parse(readFileSync(jwksPath, 'utf8'));
  if (!document || !Array.isArray(document.keys) || document.keys.length < 1) fail('invalid_jwks_document');
  if (JSON.stringify(document).includes('"d"')) fail('jwks_exposes_private_scalar');
  const matches = document.keys.filter(key => key && key.kid === privateJwk.kid && key.kty === 'EC' && key.alg === 'ES256' && key.crv === 'P-256' && key.x === privateJwk.x && key.y === privateJwk.y);
  if (matches.length !== 1) fail('jwks_signing_key_mismatch');
}
function writeAtomic(path, value) {
  const temp = `${path}.tmp-${process.pid}`;
  let fd;
  try {
    fd = openSync(temp, constants.O_CREAT | constants.O_EXCL | constants.O_WRONLY, 0o600);
    writeFileSync(fd, JSON.stringify(value), { encoding: 'utf8' });
    closeSync(fd); fd = undefined;
    enforceMode(temp, 0o600);
    renameSync(temp, path);
    enforceMode(path, 0o600);
  } finally {
    if (fd !== undefined) closeSync(fd);
    rmSync(temp, { force: true });
  }
}

try {
  const [command, ...args] = process.argv.slice(2);
  if (command === 'validate-tls' && args.length === 3) validateTls(args[0], args[1], args[2]);
  else if (command === 'validate-jwks' && args.length === 3) validateJwks(args[0], args[1], args[2]);
  else if (command === 'validate-jwk' && (args.length === 2 || args.length === 3)) {
    under(args[0], args[1]);
    const normalized = normalizeJwk(JSON.parse(readFileSync(args[0], 'utf8')));
    if (args[2]) { under(args[2], args[1]); writeAtomic(args[2], normalized); }
    enforceMode(args[0], 0o600);
  } else fail('invalid_validator_arguments');
  process.stdout.write('local_asset_validation_ok\n');
} catch (error) {
  process.stderr.write(`local_asset_validation_failed:${error.message}\n`);
  process.exitCode = 1;
}
