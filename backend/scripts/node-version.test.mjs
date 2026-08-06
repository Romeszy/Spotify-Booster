import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

let checker=fileURLToPath(new URL('./check-node-version.ps1',import.meta.url));
const shell=['pwsh','powershell.exe'].find(candidate => spawnSync(candidate,['-NoProfile','-Command','$PSVersionTable.PSVersion.Major'],{encoding:'utf8'}).status===0);
if (shell === 'powershell.exe' && process.platform !== 'win32') checker=spawnSync('wslpath',['-w',checker],{encoding:'utf8'}).stdout.trim();
function check(version){ return spawnSync(shell,['-NoProfile','-ExecutionPolicy','Bypass','-File',checker,'-VersionText',version],{encoding:'utf8'}); }

test('bootstrap Node matrix is enforced by the actual PowerShell checker',()=>{
  assert.ok(shell,'PowerShell prerequisite missing');
  assert.notEqual(check('v19.9.0').status,0);
  assert.equal(check('v20.0.0').status,0);
  assert.equal(check('v24.15.0').status,0);
  assert.notEqual(check('19.9.0').status,0);
  assert.notEqual(check('vNext').status,0);
});
