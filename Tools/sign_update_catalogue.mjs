// Publisher-only signing; the private key is never copied into source or player files.
import fs from 'node:fs';
import crypto from 'node:crypto';
const args=process.argv.slice(2);
if(args.length!==3)throw new Error('Usage: node Tools/sign_update_catalogue.mjs catalogue.unsigned.json /path/to/private-key.pem output.signed.json');
const [input,keyPath,output]=args;
const payload=fs.readFileSync(input);const catalogue=JSON.parse(payload);
if(catalogue.format!=='EarthUnscriptedUpdates'||catalogue.schemaVersion!==1||!Number.isSafeInteger(catalogue.sequence))throw new Error('Invalid catalogue');
const key=crypto.createPrivateKey(fs.readFileSync(keyPath));
const envelope={format:'EarthUnscriptedSignedUpdates',schemaVersion:1,keyId:'earth-release-rsa-2026-1',payload:payload.toString('base64'),signature:crypto.sign('sha256',payload,{key,padding:crypto.constants.RSA_PKCS1_PADDING}).toString('base64')};
fs.writeFileSync(output,JSON.stringify(envelope,null,2)+'\n');
console.log(`Signed release ${catalogue.game.version}, sequence ${catalogue.sequence}.`);
