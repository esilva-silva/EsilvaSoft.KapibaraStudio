# Conceitos de melhorias

Os itens abaixo organizam possibilidades de evolução sem atribuir fases, versões ou datas. “Existente” descreve capacidade atual ou caminho parcial; “Extensão” é uma ideia futura e não uma declaração de escopo aprovado. Para o estado atual, consulte primeiro o [estado e requisitos do MVP](../MVP/02-requisitos-e-estado.md); as fontes legadas citadas em cada item são complementares e preservam contexto técnico anterior.

## MEL-001 — Outros providers de agentes

- **Benefício:** permitir que equipes escolham outros agentes e serviços externos conforme seus fluxos e contas.
- **Existente:** há providers e código de maturidades diferentes. Claude Code preserva autenticação oficial; Codex é experimental. A composição e os gates atuais continuam como estão.
- **Extensão:** avaliar cada integração e sua experiência depois do MVP, sem duplicar registry, permissões ou autenticação existentes.
- **Dependências e limites:** homologação de runtime, conta, segurança e tools. Integrações adiadas permanecem no backlog, sem fase ou versão comprometida; não remover ou desativar código/UI. Tools de agente não consultam documentos nem escrevem diretamente no MongoDB. Consulte [decisões vigentes](../MVP/08-decisoes-vigentes.md) e [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md); como contexto complementar, veja o [backlog histórico de integrações](../../docs/backlog/bkl-06-integracoes-agentes.md) e as [ADRs 066, 068 e 070](../../docs/10-decisoes-arquiteturais.md).

## MEL-002 — Fluxos de scripts entre conexões

- **Benefício:** facilitar tarefas repetíveis que percorrem mais de uma conexão ou ambiente.
- **Existente:** navegação e execução de scripts entre conexões já existem; exemplos e limites ainda precisam de consolidação/validação.
- **Extensão:** melhorar exemplos, orientação e tratamento de falhas para fluxos manuais revisáveis.
- **Dependências e limites:** capturar conexão e contexto antes da execução assíncrona e manter isolamento entre abas. Não propor sincronização automática ou retomada como extensão implícita. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md) e [guia de uso](../MVP/05-guia-de-uso.md); como referência técnica complementar, veja o [backlog de scripts entre conexões](../../docs/backlog/bkl-03-script-engine-entre-conexoes.md).

## MEL-003 — Explain e diagnóstico de consultas

- **Benefício:** ajudar a entender o plano de execução e investigar consultas lentas ou inesperadas.
- **Existente:** há caminho parcial de explain/diagnóstico; execução de explain por agente não é permitida. Agentes só podem acessar diagnósticos de execução humana capturados e autorizados.
- **Extensão:** avaliar acesso simples pelo Console e apresentação útil dos resultados antes de considerar visualização dedicada.
- **Dependências e limites:** compatibilidade com servidor e formato de explain; explain gráfico/SBE e editor visual não estão concluídos. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md), [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md) e [decisões vigentes](../MVP/08-decisoes-vigentes.md); como referência técnica complementar, veja o [backlog de agregação](../../docs/backlog/bkl-04-modo-aggregation.md) e a [segurança de tools](../../docs/architecture/agent-tool-safety.md).

## MEL-004 — Exportar consultas ou pipelines

- **Benefício:** reaproveitar lógica de consulta em aplicações e ferramentas de desenvolvimento.
- **Existente:** o Studio oferece Console JavaScript com `aggregate`; isso não equivale a exportação de consulta para C# ou mongosh.
- **Extensão:** avaliar exportadores para formatos escolhidos com exemplos e validação de fidelidade.
- **Dependências e limites:** preservar semântica BSON, UUIDs, parâmetros e diferenças entre APIs/drivers; não prometer conversão equivalente antes de validar os casos suportados. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md), [guia de uso](../MVP/05-guia-de-uso.md) e [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md); como fontes legadas complementares, veja o [guia antigo do Console](../../docs/20-console.md) e o [status anterior do MVP](../../docs/mvp-status.md).

## MEL-005 — Transações, operações em lote e comandos BSON

- **Benefício:** apoiar operações coordenadas e repetitivas em diversos namespaces.
- **Existente:** edição e operações sobre documentos/coleções existem no recorte atual do Studio; transações explícitas e bulk multinamespace não são capacidades concluídas.
- **Extensão:** considerar transações, operações em lote e comandos BSON genéricos somente diante de necessidades concretas.
- **Dependências e limites:** suporte da topologia/servidor, tratamento de falha parcial, proteções de escrita, confirmação e auditoria. Cancelamento não deve ser descrito como rollback. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md) e [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md); como fontes complementares, veja os [requisitos antigos sem fase](../../docs/backlog/bkl-05-requisitos-sem-fase.md) e as [instruções do repositório](../../AGENTS.md).

## MEL-006 — Comparação entre bancos

- **Benefício:** revisar diferenças de dados ou estrutura antes de uma mudança operacional.
- **Existente:** exploração, consulta e gestão de objetos MongoDB existem no Studio; comparação entre bancos não está pronta.
- **Extensão:** comparação com revisão humana para dados e estrutura, com relatório claro das diferenças.
- **Dependências e limites:** escopo de leitura, volume, amostragem, igualdade BSON, permissões e confirmação humana. Não incluir sincronização automática como consequência implícita. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md) e [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md); como fontes legadas complementares, veja os [requisitos antigos sem fase](../../docs/backlog/bkl-05-requisitos-sem-fase.md) e o [status anterior do MVP](../../docs/mvp-status.md).

## MEL-007 — Jornadas especializadas do MongoDB

- **Benefício:** tornar mais acessíveis recursos que exigem consultas, visualizações ou controles próprios.
- **Existente:** exploração de metadados e recursos gerais do Studio não equivalem a jornadas completas para Search/Vector Search, GridFS, séries temporais ou change streams.
- **Extensão:** avaliar cada jornada separadamente, guiada por necessidade de uso.
- **Dependências e limites:** compatibilidade por versão/serviço MongoDB, contratos próprios e validação de segurança e volume. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md), [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md) e [guia de uso](../MVP/05-guia-de-uso.md); como fontes complementares, veja os [requisitos antigos sem fase](../../docs/backlog/bkl-05-requisitos-sem-fase.md) e o [catálogo funcional legado](../../docs/03-catalogo-funcional.md).

## MEL-008 — SSH e autenticações especializadas

- **Benefício:** conectar a ambientes que exigem um caminho de rede ou mecanismo de autenticação específico.
- **Existente:** conexão por connection string faz parte do uso atual. Segredos de conexão seguem o armazenamento protegido existente, enquanto referências a variáveis de ambiente podem manter o valor como texto puro na sessão; SSH de conexão e autenticações especializadas permanecem possibilidades futuras.
- **Extensão:** considerar mecanismos específicos quando houver necessidade concreta e contrato seguro.
- **Dependências e limites:** definir limites de proteção para segredos e variáveis, compatibilidade de driver/plataforma, operação de túnel e diagnóstico de falhas. Não confundir SSH para conexão com administração remota do host. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md), [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md) e [guia de uso](../MVP/05-guia-de-uso.md); como fontes complementares, veja [compatibilidade legada](../../docs/04-compatibilidade-e-capacidades.md) e [requisitos antigos sem fase](../../docs/backlog/bkl-05-requisitos-sem-fase.md).

## MEL-009 — Aceleração GPU/NPU e otimização de modelos

- **Benefício:** reduzir latência ou ampliar os modelos locais viáveis em equipamentos compatíveis.
- **Existente:** inferência CPU é o caminho de referência para a IA local no Studio; suporte e evidência de aceleração variam por pacote e hardware.
- **Extensão:** otimizar e homologar GPU/NPU por combinação real de pacote, runtime, sistema e dispositivo.
- **Dependências e limites:** hardware e provider efetivos, qualidade, memória, latência, cancelamento e fallback. Não bloquear o recorte CPU nem prometer suporte universal. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md), [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md) e [decisões vigentes](../MVP/08-decisoes-vigentes.md); como fontes de fase complementares, veja [IA local](../../docs/phases/phase-08-v0.12.0/README.md), [preparação KapiCoder-Mongo](../../docs/phases/phase-07a-kapicoder-mongo/README.md) e [homologação](../../docs/phases/phase-10-v0.14.0/README.md).

## MEL-010 — Extensões de pesquisa e avaliação no KapiLab

- **Benefício:** produzir evidências reproduzíveis para contratos, modelos, desempenho e hardware usados pelo Studio.
- **Existente:** KapiLab e sua infraestrutura de avaliação CPU fazem parte do trabalho necessário para desenvolver e avaliar IA local; ficam fora da experiência obrigatória do Studio. A avaliação CPU e a infraestrutura mínima do laboratório permanecem necessárias ao MVP de IA local.
- **Extensão:** ampliar pesquisa, matrizes, paridade, fila de GPU e avaliação de hardware conforme houver ambiente e caso de uso.
- **Dependências e limites:** ferramentas e pacotes disponíveis, fixtures sintéticas e evidências reais claramente separadas. GPU/NPU e pesquisa adicional são opcionais; benchmarks são manuais, nunca CI/release. KapiLab não acessa dados de usuário nem substitui o aceite do Studio. Consulte [requisitos e estado](../MVP/02-requisitos-e-estado.md), [arquitetura e invariantes](../MVP/03-arquitetura-e-invariantes.md) e a [fase vigente de IA e agentes](../MVP/fases/fase-02-v0.11.0.md); como fontes complementares, veja a [Fase 7B — KapiLab](../../docs/phases/phase-07b-kapilab/README.md), [ADR-071](../../docs/10-decisoes-arquiteturais.md) e a [Fase 8 — IA local legada](../../docs/phases/phase-08-v0.12.0/README.md).
