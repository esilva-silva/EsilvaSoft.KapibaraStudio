// Legacy command name retained for Markdown documentation link validation.
// The historical HTML reader was removed when documentation was consolidated.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '..');
const tracked = execFileSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
function collect(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(directory, entry.name);
    return entry.isDirectory() ? collect(full) : entry.name.endsWith('.md') ? [full] : [];
  });
}
const files = new Set([...tracked.filter(file => file.endsWith('.md')).map(file => path.join(root, file)).filter(file => fs.existsSync(file)), ...collect(path.join(root, 'docs'))]);
const failures = [];
let links = 0;
function exactPath(target) {
  const relative = path.relative(root, target);
  if (relative.startsWith('..') || path.isAbsolute(relative)) return false;
  let directory = root;
  for (const segment of relative.split(path.sep).filter(Boolean)) {
    if (!fs.existsSync(directory) || !fs.statSync(directory).isDirectory() || !fs.readdirSync(directory).includes(segment)) return false;
    directory = path.join(directory, segment);
  }
  return fs.existsSync(target);
}
function anchors(text) {
  const counts = new Map();
  return new Set([...text.matchAll(/^#{1,6}\s+(.+)$/gm)].map(match => {
    const slug = match[1].trim().toLowerCase().replace(/<[^>]+>/g, '').replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-');
    const count = counts.get(slug) || 0;
    counts.set(slug, count + 1);
    return count ? `${slug}-${count}` : slug;
  }));
}
for (const file of files) {
  const source = fs.readFileSync(file, 'utf8').replace(/^(```|~~~)[\s\S]*?^\1[^\n]*$/gm, '');
  for (const match of source.matchAll(/!?\[[^\]\n]*\]\(([^\n]+?)\)/g)) {
    const reference = match[1].replace(/^<(.+)>$/, '$1');
    if (/^(?:[a-z][a-z0-9+.-]*:|\/\/)/i.test(reference)) continue;
    const [encoded, fragment] = reference.split('#');
    const target = encoded ? path.resolve(path.dirname(file), decodeURIComponent(encoded)) : file;
    links++;
    let error;
    if (!exactPath(target)) error = `destino ausente ou caixa incorreta: ${reference}`;
    else if (fragment && target.endsWith('.md') && !anchors(fs.readFileSync(target, 'utf8')).has(decodeURIComponent(fragment))) error = `âncora ausente: ${reference}`;
    if (error) failures.push(`${path.relative(root, file)}: ${error}`);
  }
}
if (failures.length) {
  console.error(failures.join('\n'));
  process.exitCode = 1;
} else console.log(`PASS: ${files.size} arquivos Markdown, ${links} links locais/âncoras e caixa dos caminhos verificados.`);
