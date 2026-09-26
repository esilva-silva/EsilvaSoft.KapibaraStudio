# Protocolo de desenvolvimento da Fase 7

Aplica-se a todos os especialistas que trabalham na v0.11.0. Estes são agentes de desenvolvimento do repositório; não habilitam `SubAgents`, shell ou edição de arquivos no produto.

## Retomada e fontes

1. Ler `AGENTS.md` e as instruções locais dos diretórios alterados; conferir `git status` e preservar mudanças existentes.
2. Ler o [plano](../docs/phases/phase-07-v0.11.0/10-plano-de-implementacao.md), os [aceites](../docs/phases/phase-07-v0.11.0/12-criterios-de-aceite.md) e a [memória](../docs/memory/phase-7.md). Memória é histórico: confirmar cada afirmação no código e nos resultados atuais. Restrições de uma execução anterior não autorizam nem proíbem automaticamente ações numa nova sessão; conferir as instruções atuais.
3. Usar a [matriz de responsáveis](../docs/phases/phase-07-v0.11.0/20-agentes-e-execucao.md) e ler apenas os contratos temáticos necessários. Divergência entre memória, plano e código deve ser registrada antes de alterar o estado de um gate.

## Despacho e propriedade

Cada tarefa recebe `Task` no formato `P7-Lxx-nn`, lote, objetivo, dependências/gates, requisitos e ACs relacionados, arquivos permitidos, arquivos somente para consulta, proprietário dos contratos compartilhados, cenários de falha e saída esperada. Incluir restrições atuais do ambiente e evidências prévias com data; nunca enviar credenciais, dumps ou prompts privados.

O orquestrador permanece na sessão principal. Delegar somente trabalho delimitado que possa progredir independentemente. Não presumir contexto herdado nem delegação recursiva. Sem ferramenta de delegação, executar os papéis sequencialmente e declarar a ausência de revisão independente. Um único escritor por arquivo; contratos, DI, lockfiles, índices documentais e memória são integrados em série pelo responsável designado. Especialistas devolvem necessidades fora do escopo ao orquestrador.

Contrato compartilhado é estabilizado por arquitetura antes dos consumidores. Lotes 3 e 5 podem avançar em paralelo após o gate 2; lotes 7/8/9 após suas dependências no plano. Spikes e testes sintéticos podem avançar com gate pendente, desde que isolados e sem habilitar entrada de produto. Parcial não equivale a gate aprovado.

## Invariantes adicionais

- Runtime e MCP passam pelo mesmo registry. Principal vem do canal autenticado; `approved=true`, texto do modelo, descrição de tool e permissões do cliente não autorizam execução.
- Negar por padrão; revalidar perfil, geração da origem, política, namespace e destino de saída antes do despacho e da publicação. Login não consente envio de dados.
- Extended JSON literal, sem `ENV`, JavaScript ou comandos livres; BSON/UUID/Int64 preservados. Validar namespaces e operadores também em pipelines aninhados.
- Migrações aditivas no proprietário LiteDB único; cofre do SO armazena segredos, banco guarda referências. Falhas de persistência visíveis e recuperáveis, sem fallback plaintext.
- Aprovação de escrita imutável, expira e é consumida uma vez. Intenção durável antes de executar; falha após envio pode ser `OutcomeUnknown`, sem replay nem promessa de rollback.
- Snapshot antes de awaits, CTS por execução, filas/bytes/deadlines limitados, evento tardio descartado e tool result sem lock do consumidor do stream.
- Sem workflows da fase 8, shell livre, escrita/rede nativas ou subagentes do produto. Exceções regidas pela [ADR-056](../docs/10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026) (26/09/2026, sobre a ADR-054 revisada): o histórico de conversas do produto é persistido no owner LiteDB único, com redação, opt-out e apagar, sem anexos nem credenciais; edição de arquivos existe somente como proposta (`propose_file_edit`) revisada no editor, nunca gravada em disco pelo agente; no modo Claude Code só `Read`/`Glob`/`Grep` nativos (quando permitidos) e o transcript gravado pelo próprio Claude Code é limitação aceita. ONNX e autocomplete continuam independentes e offline.

## Entrega e gate

Retornar o formato de [agents/README.md](README.md), mais: `Task`, lote, ACs afetados, arquivos alterados, comandos e exit codes, contagens (aprovados/falhos/ignorados), artefatos sanitizados, riscos, próximo passo e estado proposto do gate. Usar `SUCCESS`, `PARTIAL`, `FAILED` ou `BLOCKED` para a tarefa; o estado do gate é separado (`pendente`, `parcial`, `aprovado`).

QA verifica cenários observáveis; code-review revisa o diff sem editar; o orquestrador aceita com evidência e documentação. Falta de conta, SO, servidor ou binário gera pendência explícita, nunca aprovação por mock. Executar comandos de `AGENTS.md` nas mudanças de código; UI exige PNGs reais inspecionados nos dois temas e homologações nativas separadas. Alterações exclusivamente documentais exigem links, rastreabilidade, adapters sincronizados e índice offline; não reutilizar números históricos como testes desta mudança.

Quando o revisor só dispõe de leitura (adapter Claude), o coordenador/QA fornece diff sanitizado, revisão base/atual e logs de validação em arquivos legíveis ou no despacho. Incluir arquivos novos, comandos, exit codes e limitações; o revisor confere o código e devolve lacunas, sem precisar de shell. A revisão não deve alegar execução própria dos comandos recebidos.
