# TODO de execução futura

Nenhum item abaixo representa trabalho executado. Os IDs e os critérios estão em [README](README.md) e [inventário técnico](inventario-tecnico.md). Cada lote deve atualizar sua evidência e os documentos afetados quando for efetivamente realizado.

## L1 — Inventário e compatibilidade

- [ ] **L1-T01** Confirmar consumidores, símbolos, projetos, pacotes, DI, UI, localização, testes e dados de REM-001 a REM-009; distinguir implementação, parcial, meta e item não encontrado.
- [ ] **L1-T02** Projetar recuperação explícita de rascunhos Script e Agregação: preservar texto, origem, perfil, banco, coleção, arquivo, codificação e estado sujo; nunca converter ou executar silenciosamente.
- [ ] **L1-T03** Definir política de versões, migração aditiva quando necessária, falhas visíveis, sessão ilegível e recuperação, sem segunda conexão LiteDB; preservar opt-outs e opt-in de JSON.
- [ ] **L1-T04** Definir retenção e acesso aos dados de profiler existentes sem exclusão silenciosa; registrar dependências de agentes e contratos mantidos.
- [ ] **L1-T05** Conferir referências dos requisitos MVP 001–015, fase 01 e conceitos de melhorias, preservando providers externos e infraestrutura compartilhada.

## L2 — Modos separados (REM-006)

- [ ] **L2-T01** Implementar e testar primeiro a recuperação explícita dos rascunhos legados, inclusive falha, concorrência, recuperação, privacidade e isolamento de abas.
- [ ] **L2-T02** Retirar pontos de entrada, execução e código exclusivos dos modos, após comprovar consumidores e compatibilidade; revisar interface, localização e DI.
- [ ] **L2-T03** Comprovar Console JavaScript, `aggregate`, abertura e gravação de scripts, histórico, explain e inferência mantidos conforme seus consumidores; executar testes relevantes e inspecionar PNGs reais afetados.

## L3 — Administração avançada (REM-001)

- [ ] **L3-T01** Remover seletivamente comandos, UI, contratos e implementações exclusivas do recorte; preservar topologia e serviços usados por operações mantidas.
- [ ] **L3-T02** Aplicar a política definida para capturas existentes e validar falha/recuperação; preservar segurança, auditoria e ferramentas de agentes permitidas.
- [ ] **L3-T03** Verificar índices, coleções, views e validadores e revisar testes e renderizações afetados.

## L4 — IA automática (REM-007)

- [ ] **L4-T01** Separar geração automática dos serviços compartilhados de configuração, modelo e diagnóstico; limpar apenas referências exclusivas, DI e testes correspondentes.
- [ ] **L4-T02** Validar que digitação não provoca inferência automática e que ghost determinístico, `Ctrl+;`, agente local, prioridade e cancelamento continuam operacionais.
- [ ] **L4-T03** Tratar preferências existentes e revisar interface, localização e renderizações quando afetadas.

## L5 — Metas e verificação (REM-002–005, REM-008–009)

- [ ] **L5-T01** Revisar catálogo, fases, backlog e guia para retirar as metas definidas, identificando eventuais trechos parciais antes de qualquer remoção técnica.
- [ ] **L5-T02** Preservar conexões a topologias suportadas, configuração de ambientes e credenciais, painel Agente IA, importação/exportação lógica e inferência de schema.
- [ ] **L5-T03** Alinhar design system, ADRs, plano e acompanhamento com o comportamento efetivamente entregue, mantendo histórico e evidências.
- [ ] **L5-T04** Executar restore locked, build e testes prescritos; executar guardas e IntegrationTests manuais aplicáveis antes de push/release e registrar limites de homologação.
- [ ] **L5-T05** Verificar explicitamente que Claude, Codex, APIs externas e MCP externo continuam com composição e disponibilidade existentes.
