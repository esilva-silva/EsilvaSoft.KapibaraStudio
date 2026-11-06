# MVP, cortes e próximas metas

Revisão: **06/11/2026**. O EsilvaSoft.KapibaraStudio deve ser um **Studio — MongoDB Database Management & Development Tool**, focado em gestão cotidiana de bancos e desenvolvimento. Esta lista registra o recorte de origem da [nova documentação oficial](../docs-new/README.md). O usuário aprovou os cortes do tópico 2 para execução futura e preservou as integrações externas no tópico 3. A reorganização documental não remove código nem altera versões; fases e TODOs vigentes ficam em `docs-new`.

**Pronto** = recorte funcional implementado, sem afirmar homologação completa Windows/Linux. **Em andamento** = parcial ou com validação essencial pendente. **Não pronto** = planejado ou indisponível no fluxo do produto. O estado atual é separado da recomendação de manter ou cortar.

## 1. MVP da aplicação — manter e concluir

- **Pronto** — Cadastrar conexões, conectar/desconectar e trabalhar com mais de uma conexão; compatibilidade completa de autenticação/topologias ainda exige homologação.
- **Pronto** — Explorar bancos, coleções e índices; abrir coleção prepara uma nova consulta sem executar.
- **Pronto** — Consultar com filtro, projeção, ordenação, paginação, contagem e valores distintos.
- **Pronto** — Inserir, editar e excluir documentos, preservando BSON, UUIDs e proteção contra conflitos.
- **Pronto** — Visualizar resultados em JSON/árvore e exportar a página carregada em JSON/CSV.
- **Pronto** — Console JavaScript com variáveis, funções, tratamento de dados e chamadas MongoDB suportadas, incluindo `aggregate`.
- **Pronto** — Abrir/salvar scripts, histórico, consultas salvas e workspace local de uma pasta.
- **Pronto** — Autocomplete contextual e texto fantasma determinístico; lista com `Ctrl+Espaço`, sem depender de IA.
- **Pronto** — Criar/remover bancos e coleções, gerenciar índices, views e validadores no recorte entregue da Fase 6.
- **Pronto** — Importar JSON/NDJSON/CSV e transferir pacotes lógicos de banco no recorte da Fase 6; não equivale a backup operacional.
- **Pronto** — Abas independentes, recuperação de rascunhos, formatação, temas, atalhos e localização existentes.
- **Em andamento** — Autocomplete com IA por `Ctrl+;`: integrado; falta consolidar qualidade e desempenho com os modelos reais.
- **Em andamento** — Agente local: contratos e ferramentas integrados parcialmente; falta validar o ciclo completo com modelo real.
- **Em andamento** — Agente GitHub Copilot: há aceite funcional das ferramentas; faltam os gates residuais de conta, sessões, falhas e ambientes.
- **Em andamento** — Auto update Windows/Linux: há seleção, integridade e download real Windows verificados; falta validar atualização completa da instalação e recuperação. Linux gerenciado atualiza pelo gerenciador de pacotes.

**Síntese das metas — detalhamento oficial em [docs-new](../docs-new/MVP/fases/README.md):**

1. **Executar os cortes aprovados:** mapear dependências e compatibilidade de sessões antes de remover código; alinhar fases, catálogo e guia ao comportamento efetivamente entregue.
2. **Concluir IA local:** validar autocomplete e agente com um pacote CPU de referência, qualidade, tempo de resposta, cancelamento e liberação de memória; sem modelo, o Studio continua utilizável. Prioridade da fase nova 02, antes do Copilot na fase nova 03.
3. **Concluir Copilot:** fechar conta, conversa, propostas, permissões e recuperação com runtime oficial, reutilizando a infraestrutura atual e os aceites existentes.
4. **Fechar instalação e atualização:** testar instalar → atualizar → reabrir, falha/recuperação e preservação dos dados em Windows/Linux, respeitando o modo de instalação.
5. **Validar o uso diário:** executar conectar → consultar → editar → salvar/exportar nos dois sistemas, com falhas, concorrência, recuperação, teclado e acessibilidade; corrigir os defeitos do recorte escolhido.

Base: [entregas 1–4](done/README.md), [Fase 6](phases/phase-06-v0.10.0/README.md), [Console](20-console.md), [IA local](phases/phase-08-v0.12.0/README.md), [Copilot](phases/phase-05-v0.9.0/meta-de-encerramento.md), [updater](architecture/explorer-updater-review.md) e [homologação](16-checklist-homologacao.md).

## 2. Não essenciais — cortes aprovados para execução futura

- **Pronto** — Administração avançada existente: profiler, `killOp`, `validate`/`compact`, parâmetros runtime e usuários/papéis. **Remoção planejada**, mesmo tendo recorte entregue na Fase 6 histórica; preservar índices, coleções, views e validadores.
- **Não pronto** — Administração do host, SSH administrativo, configuração/reinício do servidor e monitoramento externo. **Retirar das metas do produto.**
- **Não pronto** — Operar replica sets/sharding, provisionar Atlas, gerir nuvem, snapshots e recuperação operacional. **Retirar das metas**; conectar e consultar esses ambientes continua no escopo.
- **Não pronto** — Cofre criptográfico genérico de ambientes e gestão de chaves/KMS. **Retirar das metas**; preservar armazenamento de configuração necessário e proteção das credenciais usadas pelo Studio.

- **Não pronto** — Chat separado por workflow da Fase 7 histórica, ainda sem fluxo definido. **Meta cancelada no novo plano**; manter o painel Agente IA existente.
- **Não pronto** — Modos separados Script/mongosh e Agregação: caminhos parciais preservados, mas ocultos. **Remoção planejada**, mantendo JavaScript e `aggregate` no Console e tratando rascunhos antigos.
- **Pronto** — Infraestrutura histórica de IA automática durante a digitação, entregue com testes simulados e retirada do editor pela ADR-076. **Limpeza planejada**, preservando autocomplete determinístico e IA explícita.
- **Não pronto** — Backup por Database Tools, sincronização automática e jobs agendados. **Retirar das metas do MVP**; manter importação/exportação lógica.
- **Não pronto** — SQL para MQL, gerador de dados, migrações versionadas, Git integrado e serviços Federation/Archive/Charts/conectores. **Retirar das metas do MVP**; inferência de schema útil ao autocomplete permanece.

Os cortes são decisões de escopo para execução futura, inclusive onde substituem o escopo da Fase 6 histórica. O [inventário técnico](../docs-new/Remover/inventario-tecnico.md) define fronteiras e preservações. Não cortar confirmações, somente leitura, auditoria, opt-outs, fidelidade BSON ou isolamento de abas. Agentes continuam sem consultar documentos nem escrever diretamente no MongoDB.

Base: [catálogo](03-catalogo-funcional.md), [backlog](backlog/README.md), [Fase 7](phases/phase-07-v0.11.0/README.md) e [ADRs 066, 070 e 076](10-decisoes-arquiteturais.md).

## 3. Melhorias interessantes — avaliar depois do MVP

- **Em andamento** — Claude, Codex, APIs externas e MCP para clientes externos: **preservar e adiar**, sem fase/versão comprometida; manter implementações, composição, disponibilidade, contratos e infraestrutura atuais.
- **Em andamento** — Scripts entre conexões: navegação e execução já existem; melhorar exemplos e validar limites. Não há sincronizador com retomada.
- **Em andamento** — Explain e diagnóstico de consultas: há caminho bruto parcial; avaliar acesso simples pelo Console antes de criar um editor visual de pipelines.
- **Não pronto** — Exportar consultas/pipelines para C# ou mongosh.
- **Não pronto** — Transações explícitas, operações em lote entre namespaces e console de comandos BSON genéricos; avaliar apenas com caso de uso concreto.
- **Não pronto** — Comparar dados e estrutura entre bancos com revisão humana, sem sincronização automática.
- **Não pronto** — Search/Vector Search, GridFS, séries temporais e change streams como jornadas especializadas; não confundir com leitura de metadados já disponível.
- **Não pronto** — SSH de conexão e autenticações especializadas, se houver necessidade concreta de acesso.
- **Em andamento** — GPU/NPU e otimizações de modelos: suporte/evidência variam por pacote e equipamento; não bloquear o recorte CPU nem prometer suporte universal.
- **Em andamento** — KapiLab: manter como ferramenta interna de avaliação necessária à IA local, sem transformá-lo em funcionalidade obrigatória do Studio ou exigir concluir toda a pesquisa para lançar o MVP.

JavaScript e tratamento de dados no Console já estão no tópico 1: não precisam de uma nova engine. Treino e publicação de pesos permanecem no projeto externo KapiCoder-Mongo.

Base: [scripts](backlog/bkl-03-script-engine-entre-conexoes.md), [agregação](backlog/bkl-04-modo-aggregation.md), [requisitos especializados](backlog/bkl-05-requisitos-sem-fase.md), [Fase 7A](phases/phase-07a-kapicoder-mongo/README.md) e [KapiLab](phases/phase-07b-kapilab/README.md). Os registros específicos mais recentes prevalecem sobre resumos históricos conflitantes.
