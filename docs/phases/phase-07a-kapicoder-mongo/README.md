# Fase 7A — preparação da IDE para KapiCoder-Mongo

**Situação:** implementação parcial no checkout Windows; validação local em andamento. Linux manual, pesos reais e hardware NPU não fazem parte da meta deste chat.
**Meta:** F7A-KAPI.
**Versão:** sem release própria; etapa de preparação vinculada à Fase 8 / v0.12.0.

## Objetivo

Preparar o EsilvaSoft.KapibaraStudio para os pacotes KapiCoder-Mongo, reutilizando catálogo, runtime ONNX, `LocalAgentProvider`, política de modos, permissões e registry existentes. Separar compatibilidade dos modelos FIM, agente Qwen3, ferramentas de suporte ao treino e aceleração NPU condicionada a evidência real.

A denominação 7A identifica esta etapa solicitada pelo usuário. Ela não renumera as fases oficiais, não substitui o chat por workflow da [Fase 7](../phase-07-v0.11.0/README.md) e não muda a prioridade da [Fase 8](../phase-08-v0.12.0/README.md). IDs ADV-09 e EDT-02 são preservados.

## Estado desta execução

O escopo deste chat cobre os itens implementáveis e verificáveis neste checkout Windows: contratos e metadata aditivos, adapter Qwen3, caminho local de tool calling pelo registry e permissões existentes, apresentação transitória de raciocínio, composição Release Copilot + IA local e documentação/testes proporcionais. Preferências agora permitem selecionar separadamente o modelo local do agente, mantendo o autocomplete como padrão quando o campo fica vazio. A validação manual em Linux está explicitamente fora da meta e permanece pendente para execução humana. Pesos/pacotes externos e dispositivo NPU também não estão disponíveis como evidência nesta máquina; portanto nenhuma capacidade dependente deles é homologada. U7 entrou parcialmente: retenção local opt-in com prazo 1/7/30 dias, política aplicada no owner LiteDB sob o gate de gravação, expiração, limpeza e purge específico, subordinada ao histórico e excluída para conversas com solicitação de dados/diagnósticos capturados. A restauração agora sinaliza quando o raciocínio não foi retido, sem gravar seu conteúdo. A permissão local expõe as tools do registry sob opt-ins e limite por turno; o runtime fecha raciocínio aberto ao cancelar. As suítes Windows passaram: UnitTests 3.748 aprovados/20 ignorados e IntegrationTests 1.049 aprovados/20 ignorados. Aceites que exigem pesos reais continuam pendentes.

## Escopo e limites

- T0 Nano / T1 Small / T2 Pro: verificar compatibilidade FIM e regressões antes de propor alterações obrigatórias no runtime.
- T4 Agent / Agent-Lite: contratos, metadata, adapter Qwen3, geração incremental, parser, tools seguras e apresentação condicionada às capacidades comprovadas.
- T3 NPU: trilho opcional condicionado ao spike no hardware alvo; não bloqueia os demais pacotes.
- KapiLab e exportações de contratos: superfícies do produto nesta fase; console e integração ao pipeline detalhados na [Fase 7B](../phase-07b-kapilab/README.md), como ferramentas manuais de desenvolvimento.

Treinamento, datasets, exportação/publicação de pesos e serviços externos continuam no workspace KapiCoder-Mongo. Claude, Codex, APIs externas e MCP externo permanecem no backlog vigente. A ADR-070 aprova o destino Release com Copilot + IA local, texto de raciocínio com retenção/orçamento configuráveis e todas as tools vigentes sujeitas às permissões. O código de composição registra Copilot + IA local em Release e a suíte de integração passou nesta máquina. Nenhuma tool consulta documentos ou escreve diretamente no MongoDB.

## Documentos de execução

- [Meta de implementação, lotes, decisões e aceite](meta-de-implementacao.md).
- [Rastreabilidade dos 78 itens de origem](rastreabilidade-f7a.md).
- [Meta KapiLab — Fase 7B](../phase-07b-kapilab/meta-de-implementacao.md), com divisão de responsabilidade para catálogo, prompts, harness e relatórios.
- [Plano oficial](../../09-plano-de-implementacao.md), [catálogo](../../03-catalogo-funcional.md), [ADRs](../../10-decisoes-arquiteturais.md), [matriz](../../15-matriz-de-validacao.md) e [design system](../../17-design-system-ui-ux.md).

## Conclusão

O aceite automatizável exige contratos integrados e evidência por item. Pesos, hardware e jornadas nativas têm aceite próprio na Fase 10; sua ausência impede anunciar suporte homologado. Itens condicionais retirados precisam de decisão e motivo registrados, sem serem marcados como implementados.
