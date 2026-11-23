namespace HrmsAi.Chat;

// The /knowledgebase page. It holds no data; every action calls the API with the key typed into the page.
public static class KnowledgePage
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Knowledge base</title>
<style>
:root{color-scheme:light dark;--bg:#f5f6f8;--card:#fff;--text:#1d2330;--muted:#6b7385;--line:#dfe3ea;--accent:#2f6fed;--danger:#d64545;--ok:#1f9d55}
@media (prefers-color-scheme:dark){:root{--bg:#14171d;--card:#1d222b;--text:#e8ebf1;--muted:#9aa3b5;--line:#2e3542;--accent:#5b8def;--danger:#ef6b6b;--ok:#3fbf7a}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
.wrap{max-width:900px;margin:0 auto;padding:24px 16px 60px}
h1{font-size:22px;margin:0 0 4px}
.sub{color:var(--muted);margin:0 0 20px}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px;margin-bottom:16px}
label{display:block;font-weight:600;margin:0 0 4px;font-size:13px}
input[type=text],input[type=password],textarea{width:100%;padding:9px 10px;border:1px solid var(--line);border-radius:7px;background:var(--bg);color:var(--text);font:inherit}
textarea{min-height:220px;resize:vertical;font-family:ui-monospace,Consolas,monospace;font-size:13px}
input:focus,textarea:focus{outline:2px solid var(--accent);outline-offset:-1px}
.row{display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap}
.row>.grow{flex:1;min-width:200px}
button{padding:9px 16px;border:0;border-radius:7px;background:var(--accent);color:#fff;font:inherit;font-weight:600;cursor:pointer}
button.ghost{background:transparent;color:var(--text);border:1px solid var(--line)}
button.danger{background:transparent;color:var(--danger);border:1px solid var(--line)}
button.small{padding:4px 10px;font-size:13px}
button:disabled{opacity:.5;cursor:default}
.tabs{display:flex;gap:6px;margin-bottom:14px}
.tab{padding:7px 14px;border-radius:7px;border:1px solid var(--line);background:transparent;color:var(--text);cursor:pointer;font-weight:600}
.tab.on{background:var(--accent);border-color:var(--accent);color:#fff}
.hide{display:none}
.field{margin-bottom:12px}
.hint{color:var(--muted);font-size:12px;margin-top:4px}
.msg{margin:0 0 16px;padding:10px 12px;border-radius:7px;border:1px solid var(--line);background:var(--card)}
.msg.ok{border-color:var(--ok)}.msg.err{border-color:var(--danger)}
table{width:100%;border-collapse:collapse}
th,td{text-align:left;padding:9px 6px;border-bottom:1px solid var(--line);vertical-align:middle}
th{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
td.actions{text-align:right;white-space:nowrap}
.empty{color:var(--muted);padding:12px 0}
.editing{background:rgba(47,111,237,.12);border:1px solid var(--accent);padding:8px 12px;border-radius:7px;margin-bottom:12px}
</style>
</head>
<body>
<div class="wrap">
  <h1>Knowledge base</h1>
  <p class="sub">The chat assistant answers HR questions from the documents you keep here.</p>

  <div class="card" id="keyCard">
    <div class="row">
      <div class="grow">
        <label for="key">API key</label>
        <input type="password" id="key" placeholder="hrms_..." autocomplete="off">
      </div>
      <button id="connect">Connect</button>
      <button class="ghost hide" id="disconnect">Disconnect</button>
    </div>
    <div class="hint">The key is only kept in this browser tab. You will see and change only the documents of this key.</div>
  </div>

  <div id="msg" class="msg hide"></div>

  <div id="app" class="hide">
    <div class="card">
      <div class="tabs">
        <button class="tab on" id="tabText">Add text</button>
        <button class="tab" id="tabFile">Upload files</button>
      </div>

      <div id="paneText">
        <div id="editBar" class="editing hide"></div>
        <div class="field">
          <label for="title">Title</label>
          <input type="text" id="title" maxlength="200" placeholder="For example: How to apply for leave">
          <div class="hint">Saving with a title that already exists replaces that document.</div>
        </div>
        <div class="field">
          <label for="text">Text</label>
          <textarea id="text" placeholder="Write the steps or the policy here. Use a blank line between topics."></textarea>
          <div class="hint"><span id="count">0</span> / 300000 characters</div>
        </div>
        <div class="row">
          <button id="save">Save</button>
          <button class="ghost hide" id="cancelEdit">Cancel edit</button>
        </div>
      </div>

      <div id="paneFile" class="hide">
        <div class="field">
          <label for="files">Files (.txt or .md)</label>
          <input type="file" id="files" accept=".txt,.md" multiple>
          <div class="hint">Each file becomes one document. The title is the file name unless you type one below (one file only).</div>
        </div>
        <div class="field">
          <label for="fileTitle">Title (optional)</label>
          <input type="text" id="fileTitle" maxlength="200">
        </div>
        <button id="upload">Upload</button>
      </div>
    </div>

    <div class="card">
      <label>Your documents</label>
      <div id="list"></div>
    </div>
  </div>
</div>

<script>
const $ = id => document.getElementById(id);
let key = '';
try { key = sessionStorage.getItem('kbKey') || ''; } catch (e) {}
let editingId = null, editingTitle = null;

function show(text, kind) {
  const m = $('msg');
  m.textContent = text;
  m.className = 'msg ' + (kind || '');
}
function hideMsg() { $('msg').className = 'msg hide'; }

async function api(path, opts) {
  opts = opts || {};
  opts.headers = Object.assign({ 'X-API-Key': key }, opts.headers || {});
  const r = await fetch(path, opts);
  let body = null;
  try { body = await r.json(); } catch (e) {}
  if (!r.ok) {
    if (r.status === 401 || r.status === 403) throw new Error('This key is not accepted (or not allowed for chat).');
    throw new Error((body && (body.error || body.message)) || ('Error ' + r.status));
  }
  return body;
}

function setConnected(on) {
  $('app').classList.toggle('hide', !on);
  $('disconnect').classList.toggle('hide', !on);
  $('connect').classList.toggle('hide', on);
  $('key').disabled = on;
}

async function connect() {
  key = $('key').value.trim() || key;
  if (!key) { show('Enter your API key first.', 'err'); return; }
  try {
    await loadList();
    try { sessionStorage.setItem('kbKey', key); } catch (e) {}
    $('key').value = '********';
    setConnected(true);
    hideMsg();
  } catch (e) {
    setConnected(false);
    show(e.message, 'err');
  }
}

function disconnect() {
  key = '';
  try { sessionStorage.removeItem('kbKey'); } catch (e) {}
  $('key').value = '';
  setConnected(false);
  resetForm();
  hideMsg();
}

function fmtDate(s) {
  if (!s) return '';
  const d = new Date(/Z|[+-]\d\d:\d\d$/.test(s) ? s : s + 'Z');
  return d.toLocaleString();
}

async function loadList() {
  const docs = await api('/api/chat/documents');
  const box = $('list');
  box.textContent = '';
  if (!docs.length) {
    const e = document.createElement('div');
    e.className = 'empty';
    e.textContent = 'No documents yet. Add some text or upload a file above.';
    box.appendChild(e);
    return;
  }
  const table = document.createElement('table');
  const head = document.createElement('tr');
  ['Title', 'Pieces', 'Added', ''].forEach(h => { const th = document.createElement('th'); th.textContent = h; head.appendChild(th); });
  table.appendChild(head);
  docs.forEach(d => {
    const tr = document.createElement('tr');
    const t = document.createElement('td'); t.textContent = d.title + (d.fileName ? '  (' + d.fileName + ')' : '');
    const c = document.createElement('td'); c.textContent = d.chunks;
    const a = document.createElement('td'); a.textContent = fmtDate(d.createdAt);
    const x = document.createElement('td'); x.className = 'actions';
    const edit = document.createElement('button'); edit.className = 'ghost small'; edit.textContent = 'Edit';
    edit.onclick = () => startEdit(d.id);
    const del = document.createElement('button'); del.className = 'danger small'; del.textContent = 'Delete';
    del.style.marginLeft = '6px';
    del.onclick = () => remove(d.id, d.title);
    x.appendChild(edit); x.appendChild(del);
    [t, c, a, x].forEach(n => tr.appendChild(n));
    table.appendChild(tr);
  });
  box.appendChild(table);
}

function resetForm() {
  editingId = null; editingTitle = null;
  $('title').value = ''; $('text').value = '';
  $('editBar').classList.add('hide');
  $('cancelEdit').classList.add('hide');
  updateCount();
}

function updateCount() { $('count').textContent = $('text').value.length; }

async function startEdit(id) {
  try {
    const d = await api('/api/chat/documents/' + id);
    editingId = d.id; editingTitle = d.title;
    $('title').value = d.title; $('text').value = d.text;
    $('editBar').textContent = 'Editing: ' + d.title;
    $('editBar').classList.remove('hide');
    $('cancelEdit').classList.remove('hide');
    switchTab('text');
    updateCount();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  } catch (e) { show(e.message, 'err'); }
}

async function save() {
  const title = $('title').value.trim();
  const text = $('text').value.trim();
  if (!title) { show('A title is required.', 'err'); return; }
  if (!text) { show('Write some text first.', 'err'); return; }
  $('save').disabled = true;
  try {
    const r = await api('/api/chat/documents', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, text })
    });
    // Title changed while editing: the old document is removed so it is not duplicated.
    if (editingId && editingTitle !== title) { try { await api('/api/chat/documents/' + editingId, { method: 'DELETE' }); } catch (e) {} }
    show('Saved "' + r.title + '" (' + r.chunks + ' pieces).', 'ok');
    resetForm();
    await loadList();
  } catch (e) { show(e.message, 'err'); }
  finally { $('save').disabled = false; }
}

async function upload() {
  const files = Array.from($('files').files);
  if (!files.length) { show('Choose at least one file.', 'err'); return; }
  const customTitle = $('fileTitle').value.trim();
  $('upload').disabled = true;
  const done = [], failed = [];
  for (const f of files) {
    const fd = new FormData();
    fd.append('file', f);
    const title = (files.length === 1 && customTitle) ? customTitle : f.name.replace(/\.[^.]+$/, '');
    fd.append('title', title);
    try { await api('/api/chat/documents', { method: 'POST', body: fd }); done.push(f.name); }
    catch (e) { failed.push(f.name + ': ' + e.message); }
  }
  $('files').value = ''; $('fileTitle').value = '';
  show((done.length ? 'Uploaded ' + done.length + ' file(s). ' : '') + (failed.length ? 'Failed: ' + failed.join(' | ') : ''), failed.length ? 'err' : 'ok');
  $('upload').disabled = false;
  try { await loadList(); } catch (e) {}
}

async function remove(id, title) {
  if (!confirm('Delete "' + title + '"? The chat will stop using it.')) return;
  try {
    await api('/api/chat/documents/' + id, { method: 'DELETE' });
    if (editingId === id) resetForm();
    show('Deleted "' + title + '".', 'ok');
    await loadList();
  } catch (e) { show(e.message, 'err'); }
}

function switchTab(which) {
  $('paneText').classList.toggle('hide', which !== 'text');
  $('paneFile').classList.toggle('hide', which !== 'file');
  $('tabText').classList.toggle('on', which === 'text');
  $('tabFile').classList.toggle('on', which === 'file');
}

$('connect').onclick = connect;
$('disconnect').onclick = disconnect;
$('key').addEventListener('keydown', e => { if (e.key === 'Enter') connect(); });
$('save').onclick = save;
$('cancelEdit').onclick = () => { resetForm(); hideMsg(); };
$('upload').onclick = upload;
$('tabText').onclick = () => switchTab('text');
$('tabFile').onclick = () => switchTab('file');
$('text').addEventListener('input', updateCount);

if (key) { $('key').value = key; connect(); }
</script>
</body>
</html>
""";
}