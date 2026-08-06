import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

let script=fileURLToPath(new URL('./process-tree-timeout.test.ps1',import.meta.url));
const shell=['pwsh','powershell.exe'].find(candidate=>spawnSync(candidate,['-NoProfile','-Command','$PSVersionTable.PSVersion.Major'],{encoding:'utf8'}).status===0);
if (shell==='powershell.exe'&&process.platform!=='win32') script=spawnSync('wslpath',['-w',script],{encoding:'utf8'}).stdout.trim();

test('timeout kills the wrapper and its spawned child before fixture cleanup',()=>{
  assert.ok(shell,'PowerShell prerequisite missing');
  const result=spawnSync(shell,['-NoProfile','-ExecutionPolicy','Bypass','-File',script],{encoding:'utf8',timeout:15000});
  assert.equal(result.status,0,result.stderr);
  assert.match(result.stdout,/process_tree_timeout_pass/);
});
