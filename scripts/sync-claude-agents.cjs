// Keep Claude Code adapters thin and derived from the canonical agent contracts.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const check = process.argv.includes('--check');
const sections = ['Name', 'Purpose', 'Responsibilities', 'Inputs', 'Outputs', 'Allowed Actions',
  'Restrictions', 'Preferred Model Capability', 'Alternative Model Capability', 'Example Models',
  'When to Use', 'When Not to Use', 'Dependencies', 'Validation Rules'];
const expected = new Map();
for (const file of fs.readdirSync(path.join(root, 'agents')).filter(file => file.endsWith('.md')).sort()) {
  const source = fs.readFileSync(path.join(root, 'agents', file), 'utf8')
    .replaceAll('\r\n', '\n').replace(/^```[^\n]*\n[\s\S]*?^```\s*$/gm, '');
  if (!/^## Name$/m.test(source)) continue;
  for (const section of sections) {
    if (!source.includes(`\n## ${section}\n`)) throw new Error(`${file}: seção ausente: ${section}`);
  }
  const name = source.match(/^## Name\n+`([a-z0-9-]+)`/m)?.[1];
  if (!name || file !== `${name}.md`) throw new Error(`${file}: Name inválido`);
  if (name === 'goal-orchestrator') continue;
  const description = source.match(/^## Purpose\n+([^\n]+)/m)?.[1].replaceAll('**', '');
  if (!description) throw new Error(`${file}: Purpose vazio`);
  const readOnlyTools = name === 'code-review-agent' ? 'tools: Read, Glob, Grep\n' : '';
  expected.set(file, `---\nname: ${name}\ndescription: ${JSON.stringify(description)}\n${readOnlyTools}model: inherit\npermissionMode: default\n---\n\n` +
    `<!-- Gerado por scripts/sync-claude-agents.cjs; edite o contrato canônico. -->\n\n` +
    `Leia integralmente \`AGENTS.md\`, as instruções locais aplicáveis e \`agents/${file}\` antes de agir.\n` +
    `Na Fase 7, leia também \`agents/phase-7-protocol.md\` e a tarefa/lote em\n` +
    `\`docs/phases/phase-07-v0.11.0/20-agentes-e-execucao.md\`.\n\n` +
    `Trabalhe somente no recorte e arquivos atribuídos. Não presuma contexto da conversa principal.\n` +
    `Se faltar contrato, dependência ou ferramenta, devolva a pendência ao coordenador.\n` +
    `Respeite as permissões da sessão; não amplie o escopo nem delegue recursivamente.\n` +
    `Retorne no formato de \`agents/README.md\`, com evidências, limitações e estado do gate separado do estado da tarefa.\n`);
}
const target = path.join(root, '.claude', 'agents');
const failures = [];
for (const file of fs.existsSync(target) ? fs.readdirSync(target).filter(file => file.endsWith('.md')) : []) {
  if (!expected.has(file)) failures.push(`Adapter órfão (revisar manualmente): ${file}`);
}
if (!check) fs.mkdirSync(target, { recursive: true });
for (const [file, content] of expected) {
  const full = path.join(target, file);
  if (check) {
    if (!fs.existsSync(full) || fs.readFileSync(full, 'utf8').replaceAll('\r\n', '\n') !== content) {
      failures.push(`Adapter ausente ou divergente: ${file}`);
    }
  } else fs.writeFileSync(full, content);
}
if (failures.length) {
  console.error(failures.join('\n'));
  process.exitCode = 1;
} else console.log(`${expected.size} adapters Claude ${check ? 'verificados' : 'sincronizados'}.`);
