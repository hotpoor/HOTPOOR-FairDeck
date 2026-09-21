import { API_BASE } from './config.js';
const status = document.querySelector('#status');
async function get(path) {
  const response = await fetch(API_BASE + path, {signal: AbortSignal.timeout(10000), credentials: 'omit'});
  if (!response.ok) throw new Error(response.status === 404 ? '记录不存在或不公开。' : `服务请求失败（${response.status}）。`);
  return response.json();
}
async function refresh() {
  status.textContent = '正在连接…';
  document.querySelector('#nodes').replaceChildren();
  try {
    const [health, list] = await Promise.all([get('/health'), get('/v1/nodes')]);
    status.textContent = `已连接 ${health.service} · ${health.network} · 共识状态：${health.consensus}`;
    for (const node of list.nodes) {
      const li = document.createElement('li');
      li.textContent = `${node.ip.includes(':') ? `[${node.ip}]` : node.ip}:${node.port} — ${node.address}`;
      document.querySelector('#nodes').append(li);
    }
    if (!list.nodes.length) document.querySelector('#nodes').textContent = '暂无有效公开节点。';
  } catch { status.textContent = '暂时无法连接线上服务。可能尚未部署，或网络／跨域配置不可用。'; }
}
document.querySelector('#refresh').addEventListener('click', refresh);
document.querySelector('#lookup').addEventListener('submit', async event => {
  event.preventDefault(); const result = document.querySelector('#result');
  const id = document.querySelector('#block').value.trim();
  if (!/^[0-9a-f]{32}$/.test(id)) { result.textContent = '请输入 32 位小写十六进制 UUID。'; return; }
  result.textContent = '查询中…';
  try { result.textContent = JSON.stringify(await get('/v1/entities/' + id), null, 2); }
  catch (error) { result.textContent = error.message; }
});
refresh();
