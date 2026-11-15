// PreToolUse hook: agents write the board (openspec/changes) through the board_* tools (repo-tasks MCP), never by hand.
// Reads the hook JSON on stdin; denies Edit/Write/MultiEdit/NotebookEdit on a path under <repo>/openspec/changes and Bash
// commands that write there. RTM_ALLOW_DIRECT_EDIT=1 lets everything through (the user's escape hatch). No dependencies.
const path = require('path');

const MESSAGE = 'Board files are written through the board_* tools (repo-tasks MCP), not edited directly.';
const slashes = s => s.split('\\').join('/');
const norm = p => slashes(path.resolve(p)).toLowerCase();

// this repository: the folder holding .claude/, and the main tree when the script runs from a worktree of it
const here = norm(path.join(__dirname, '..', '..'));
const roots = [here, here.replace(/\/\.claude\/worktrees\/[^/]+$/, '')];
const under = (p, cwd) => {
  const full = norm(path.isAbsolute(p) ? p : path.join(cwd, p));
  return roots.some(r => full.startsWith(r + '/openspec/changes/') || full === r + '/openspec/changes');
};

// a Bash command that names the folder and writes: a redirect (not 2>&1), tee, sed -i, mv, cp, rm, git checkout...
const writes = /(?<![0-9&])>>?(?!&)|\b(tee|mv|cp|rm|touch|truncate|set-content|add-content|out-file|remove-item|new-item|move-item|copy-item|rename-item)\b|\bsed\s+(-\S*\s+)*-i|\bgit\s+(checkout|restore|apply|clean|reset|stash)\b/i;

function decide(input) {
  if (process.env.RTM_ALLOW_DIRECT_EDIT === '1') return false;
  const tool = input.tool_name, args = input.tool_input || {}, cwd = input.cwd || process.cwd();
  if (['Edit', 'Write', 'MultiEdit', 'NotebookEdit'].includes(tool))
    return [args.file_path, args.notebook_path, args.path].some(p => typeof p === 'string' && under(p, cwd));
  if (tool === 'Bash' && typeof args.command === 'string') {
    const text = slashes(args.command);
    return /openspec\/changes/i.test(text) && writes.test(text);
  }
  return false;
}

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', d => raw += d);
process.stdin.on('end', () => {
  let input = {};
  try { input = JSON.parse(raw); } catch { /* not ours: allow */ }
  if (decide(input))
    process.stdout.write(JSON.stringify({ hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: 'deny', permissionDecisionReason: MESSAGE } }));
});
