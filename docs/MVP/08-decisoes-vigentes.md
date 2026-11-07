# Decisões vigentes

Estas decisões definem o plano e os contratos vigentes. Detalhes normativos estão nos contratos de [tools](contratos/tools-agentes.md), [arquivos](contratos/arquivos-agentes.md), [IA local](contratos/ia-local.md), na [arquitetura](03-arquitetura-e-invariantes.md) e em [distribuição](07-distribuicao-e-suporte.md); resultados e limitações na [baseline](09-baseline-de-validacao.md).

| ID | Decisão | Consequência verificável |
| --- | --- | --- |
| MVP-D01 | Studio desktop para gestão cotidiana e desenvolvimento MongoDB. | Manter EsilvaSoft.KapibaraStudio, .NET 10/Avalonia, Windows/Linux, MIT e pt-BR. |
| MVP-D02 | `docs` é a única árvore oficial, autossuficiente. | Consolidar contratos e evidências necessários; não depender de links ao acervo removido ou a revisões antigas. |
| MVP-D03 | Seis fases, v0.10.0 até v1.0.0, são alvos documentais. | Nenhuma mudança automática em binário, tags, pacotes ou identidade de releases antigas. |
| MVP-D04 | IA local CPU precede conclusão Copilot. | Fase 1 delimita limpeza/base; Fase 2 fecha IA CPU; Fase 3 fecha Copilot residual. |
| MVP-D05 | O catálogo vigente define MVP-001..015. | Requisitos implementados são reaproveitados, sem reimplementação por mudança de plano. |
| MVP-D06 | Cortes do tópico 2 executados pela Task-R 1, com validação por lote. | Inventariar dependências e compatibilidade antes de remover; Console/aggregate e proteções permanecem. |
| MVP-D07 | Providers externos são preservados e adiados em Melhorias. | Não eliminar composição/contratos necessários nem criar autenticação paralela; Claude Code oficial preservado. |
| MVP-D08 | IA é opcional e autocomplete IA é explícito. | Ctrl+Espaço determinístico; Ctrl+; chama IA; ausência de modelo não bloqueia uso cotidiano. |
| MVP-D09 | Tools não consultam documentos nem escrevem MongoDB. | Apenas metadados/índices e saídas humanas capturadas; consentimento não amplia essa fronteira. |
| MVP-D10 | Evidência é específica por camada/ambiente. | Implementação e validação separadas; nenhuma homologação real inferida de Headless ou snapshots históricos. |
| MVP-D11 | Atualização respeita instalação Linux gerenciada. | Gerenciador atualiza pacote; aplicativo não sobrescreve arquivos gerenciados; dados do usuário preservados. |
| MVP-D12 | KapiLab apoia avaliação, sem virar gate de toda pesquisa. | CPU real e qualidade do Studio são gates; treino/publicação de pesos são externos. |
| MVP-D13 | Recuperação dos modos retirados conserva texto e origem. | Campo aditivo `LegacyMode` na sessão v2; abrir como Texto, sem converter/executar. Adaptação ao Console é explícita. Entrada JSON antiga permanece acessível com opt-in. Evidência na Task-R 1/L2. |

## Parâmetros ainda a fixar durante as entregas

Pacote/modelo CPU de referência, equipamento, corpus e limites de qualidade/latência/memória; matriz mínima de Windows/Linux, MongoDB, autenticações e formatos de instalação; versões oficiais Copilot compatíveis e responsáveis pelo aceite de termos. Registrar os valores e justificativas antes dos ensaios correspondentes, sem inventar números para declarar sucesso.

Mudança de comportamento exige atualizar requisito, arquitetura/design system/guia afetados, decisão e evidência. Remoção de superfície exige tratar sessões/rascunhos antigos e preservar recuperação.
