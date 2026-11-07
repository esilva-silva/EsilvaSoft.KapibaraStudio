# Fase 2 — v0.11.0: IA local CPU

**Situação:** planejada no novo plano; nenhuma execução ou release declarada por este documento. **Requisitos:** MVP-008, MVP-011..013.

## Objetivo e dependências

Concluir autocomplete explícito e ciclo do agente local no runtime vigente com pacote CPU real.

Dependências: Base/contexto seguros da Fase 1; não exige GPU/NPU ou conclusão de toda pesquisa KapiLab.

## Entregas TODO

- [ ] F02-E01 — Fixar pacote/modelo CPU, hardware, corpus e limites de qualidade, tempo e memória antes de executar avaliação.
- [ ] F02-E02 — Demonstrar Ctrl+; → prévia → Tab/Undo ou descarte, resposta obsoleta rejeitada e ausência de inferência na digitação.
- [ ] F02-E03 — Demonstrar ciclo de agente local com tools vigentes, permissões, falhas e saídas humanas capturadas, sem consulta direta a documentos.
- [ ] F02-E04 — Validar cancelamento/concorrência, liberação por inatividade/fechamento, retenção/opt-out e recuperação com modelo real.
- [ ] F02-E05 — Registrar uso sem modelo e resultados de qualidade/desempenho CPU, separando testes falsos, reais e desktop nativo.

## Evidências existentes e pendências

Os [contratos de IA local](../contratos/ia-local.md) definem prompt, contexto, EOS, cache, cancelamento e avaliação. A [baseline](../09-baseline-de-validacao.md) registra paridade de bytes/tokens, fixture kapi-sim e provas CPU, com versões e limites. Faltam qualidade/ciclo completo com pacote CPU atual, TTFT/RAM, liberação, retenção e recuperação reais. Cache permanece desativado sem aceite do pacote.

## Critério de saída

Digitar não infere. Ctrl+; produz prévia da origem capturada; Tab aceita com Undo, Escape descarta sem inserir ponto e vírgula. Sem modelo, editor/autocomplete normal funcionam. Agente completa ciclo real permitido e nega tool proibida; operações independentes preservam cancelamento/contexto, retenção e liberação segura. Resultados atendem limites previamente fixados, ou ficam falhos/pendentes.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.
