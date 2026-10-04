# Fase 7 — v0.11.0: chat simples com IA baseado em workflow

**Situação:** Planejada.

## Objetivo

Chat técnico que segue um **fluxo predefinido**, com escopo limitado e ações controladas.

## Escopo incluído (IDs do catálogo)

- ADV-09 (recorte de chat) — conversa guiada por workflow, com passos declarados e conjunto fechado de ações.
- Toda ação proposta pelo chat é revisável; nenhuma é executada automaticamente.

## Fora de escopo

Chat livre de propósito geral, execução autônoma de comandos, acesso a serviços externos e qualquer envio implícito de dados do usuário.

## Antecipações técnicas presentes no código

Existe um painel de chat experimental ligado ao runtime local (Fase 8). Ele **não** implementa o workflow desta fase e não constitui antecipação de aceite.

## Critério de aceite

Fluxo predefinido observável passo a passo; escopo de ações fechado e documentado; toda escrita exige confirmação; funcionamento degradado e explícito sem modelo instalado; nenhuma ação fora do workflow declarado.

## Dependências

Fase 6 aceita; política de dados e contexto determinístico das fases anteriores.

## Documentos relacionados

- [ONNX/chat](../../23-onnx-slopcoder.md) · [IA local multimodelo](../../26-ia-local-multimodelo.md) · [Contexto para IA](../../auto-complite/ai-context.md)

## Pendências de escopo

O workflow ainda não foi definido em documento próprio de requisitos. Nenhuma evidência de aceite existe.

**Referência técnica preservada:** a integração existente com Claude está descrita em [Claude no KapibaraStudio](../../backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento) e fica no backlog bkl-06. Sua conclusão não é dependência automática deste workflow, que preserva seu próprio conjunto fechado de ações e critérios de aceite.
