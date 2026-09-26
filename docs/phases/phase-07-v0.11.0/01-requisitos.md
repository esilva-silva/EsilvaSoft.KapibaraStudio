# Requisitos da v0.11.0

**Estado: planejamento.** As capacidades atuais estão na [análise da solução](13-analise-do-codigo.md); as linhas abaixo não são declaração de implementação.

| ID | Requisito e prioridade | Contrato/evidência de aceite |
| --- | --- | --- |
| AGT-01 | Obrigatório: runtime independente de provider, UI nativa e eventos normalizados | [Runtime](03-agent-runtime.md); AC-04/09/10/11 |
| AGT-02 | Obrigatório: adaptador OpenAI por API Key do usuário (baseline). **Login por conta ChatGPT/OpenAI (sublote 7B) é condicional e prioridade atual** aos gates de suporte/risco, confinamento e keyring, e só se declara entregue após [homologação manual](21-homologacao-manual-login.md) | [Providers](04-providers.md), [autenticação](07-autenticacao-e-segredos.md), [plano 7B](10-plano-de-implementacao.md#sublote-7b--login-por-conta-openai-condicional); AC-05/07/08 |
| AGT-03 | Obrigatório e **prioridade vigente (25/09/2026)**: Claude em dois modos separados — **assinatura pelo Claude Code** (binário oficial como subprocesso, login delegado, sem credencial no Slop) e **Anthropic API** (API Key) —, com streaming, sessão, permissões, tools e cancelamento; modo em uso sempre visível e sem fallback silencioso; sem importar tokens/sessões | [23](23-integracao-claude.md), ADR-053/054, AU-04, CC-01..08, AC-06/07/08/09/11/12; gates GCL-1..8 e homologação manual C-01..C-34 |
| AGT-14 | Obrigatório: chat completo na UI da aplicação (hospedagem na janela principal, streaming, cartões de tool, aprovação/consentimento, configuração, MCP opt-in, acessibilidade, localização). **Revisado em 26/09/2026 ([ADR-056](../../10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026), planejado):** painel global ao workspace com conversas persistidas no LiteDB (histórico, conversa selecionada, modelo, modo e preferências do painel), opt-out **Não guardar histórico** e **Apagar histórico**, sem persistir anexos nem credenciais; permissões persistentes por provider numa tela Permissões; contexto em chips (arquivo ativo por padrão); modos Agente/Planejamento/Automático/Solicitar confirmações; tools MongoDB de leitura e propostas de edição revisadas por diff; disponibilidade verificada automaticamente. Texto anterior: contexto por aba e sem transcript persistido | [Lote 6 ampliado](10-plano-de-implementacao.md#lote-6-ampliado--chat-completo-na-ui-da-aplicação), [16](16-chat-nativo.md), [polimento CLP](23-integracao-claude.md#polimento-do-agente-ia--adr-056-26092026); AC-03/04/07/09/10/11/12/14; GCL-9..GCL-17 |
| AGT-04 | Obrigatório: ONNX independente; capabilities refletem o modelo carregado | AC-16; nenhuma chamada externa como fallback |
| AGT-05 | Obrigatório: registro único de tools para MCP e runtime | [Catálogo](06-mcp-tools.md); AC-01/02/14 |
| AGT-06 | Obrigatório: conexão lógica, BSON fiel e leitura com limites | AC-03/13; credenciais resolvidas somente localmente |
| AGT-07 | Obrigatório: permissões determinísticas e aprovação antes de efeitos | [Política](08-permissoes-e-aprovacoes.md); AC-12 |
| AGT-08 | Obrigatório: cofre por SO e falha visível sem plaintext | AC-08/17; bloqueia persistência de credenciais |
| AGT-09 | Obrigatório: saída de dados explícita, auditável e limitada | [Privacidade](09-seguranca-e-privacidade.md); AC-03/13 |
| AGT-10 | Obrigatório: funcionamento degradado e cancelamento isolado | AC-11/15/16/17 |
| AGT-11 | Obrigatório: migração preserva workflow e homologação | AC-18; comparação documental |
| AGT-12 | Incremento posterior ao MCP read-only: insert/update/delete e índices protegidos | AC-12/19; nenhuma aprovação genérica |
| AGT-13 | Extensível: novo provider sem branch de fornecedor na UI | AC-09/20; Copilot futuro, sem promessa nesta versão |

## Escopo do primeiro MCP

Listar conexões autorizadas, bancos e coleções, obter schema, amostrar documentos, find, count, distinct, explain e índices. Cada tool só será exposta após o gap correspondente no catálogo ser encerrado. Leitura de metadados e leitura de documentos são permissões diferentes. Mesmo uma contagem pode revelar informação; o rótulo READ_ONLY não concede acesso.

O escopo completo prevê integração do sublote 7B e do bloco CL — Integração Claude (substitui o antigo sublote 8B), chat e credenciais seguras. O marco read-only é um incremento verificável, não o aceite automático de toda a versão. Tools administrativas e execução arbitrária de scripts/terminal do registry/MCP permanecem desabilitadas; operações administrativas só entram em lote posterior com especificação própria. A única exceção é a das ferramentas nativas do modo Claude Code com aprovação por chamada (ver [Exclusões e compatibilidade](#exclusões-e-compatibilidade) abaixo), que não amplia o registry/MCP. **Estado vigente (26/09/2026):** a exceção foi reduzida a leitura pura (ADR-054 revisada); a ADR-056 acrescenta ao registry as tools `get_cached_schema`, `get_workspace_context` e `propose_file_edit` (esta só propõe, nunca grava em disco), expostas ao Claude Code por MCP local. Streamable HTTP é extensão planejada, fora do gate mínimo STDIO; o contrato já define os requisitos para habilitá-lo.

## Exclusões e compatibilidade

Não criar um site, WebView de ChatGPT/Claude, créditos fornecidos pela aplicação, login por cookies, acesso implícito a arquivos/segredos, shell livre, editor de workflows nesta fase ou migração destrutiva do workspace. Exceção decidida pelo usuário em 25/09/2026: no modo Claude Code, ferramentas nativas (shell, arquivos, rede) com aprovação por chamada ([ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026)); continua proibido shell livre sem aprovação. **Revisto no mesmo dia (ADR-054 revisada) e em 26/09/2026 (ADR-056):** shell, escrita de arquivo e rede nativos ficam desligados por construção; só leitura pura e edição como proposta revisada no editor. O workflow controlado continua na v0.12.0 com seu escopo preservado.

Capturar perfil, revisão, banco, coleção, texto, opções e permissões antes de awaits. Explorer apenas navega. Abas existentes nunca seguem seleção do Explorer. Cada sessão/turno tem seu próprio cancelamento e correlação. Cancelamento não significa rollback. LiteDB continua com proprietário único registrado em DI; rascunhos preservam opt-out, entrada JSON opt-in e exclusão de resultados e credenciais. Uma sessão ilegível não pode ser sobrescrita.

## Rastreabilidade do pedido original

Os itens 1/27/34 do pedido são cobertos pela migração e índice; 2–7/14/18/24/25 por arquitetura/runtime/providers; 8–15/30 por servidor/catalogação/políticas; 16–17 pela UX; 19–23 por autenticação/privacidade; 26 pelas fontes/licenças; 28 pelas seis ADRs; 29 pelo plano; 31–33 por testes/aceite/degradação. A revisão externa da especificação e licenças é datada, não uma garantia permanente de compatibilidade.
