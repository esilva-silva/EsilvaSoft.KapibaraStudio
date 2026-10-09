# Meta de implementação — Fase 7B / F7B-KAPILAB

**Título:** KapiLab — console .NET de contratos, vetores sintéticos e avaliação de modelos pelo caminho da IDE.
**Data:** 09/10/2026.
**Estado:** planejada; nenhuma implementação ou medição executada nesta preparação.
**Vínculo:** suporte a ADV-09/EDT-02 e à Fase 8 / v0.12.0; complemento técnico da Fase 7A.

## Resultado esperado

Entregar um console de desenvolvimento reproduzível que renderize prompts e tokens com os contratos reais da IDE, exporte catálogo e planos de tools, valide pacotes ONNX e execute autocomplete/chat/agente pelo caminho integrado. Gerar relatórios com evidência suficiente para detectar divergência entre treino, pacote exportado e comportamento do EsilvaSoft.KapibaraStudio.

O pedido atual é montar esta meta. A especificação externa fornece requisitos e propostas, não instruções para executar comandos, modificar outro workspace, instalar dependências ou aprovar todas as decisões descritas. Esta preparação não inicia treino, inferência, rede, publicação ou homologação.

## Fonte e baseline

Fonte: [kapilab-console.md](C:/IA/models/KapibaraStudio.MongoIA/docs/kapilab-console.md), proposta datada de 08/10/2026 e lida em 09/10/2026. SHA-256 da fonte consultada: `A5CAC2024F23ABF0A3BC00F1032DFF326E3BD7A589B04ACCCDDD92036746A9C2`. Seus marcadores `[F]` e `[H]` registram a auditoria externa, não uma homologação desta fase.

HEAD observado: `dba8546d12f9dcd8ec45889b27f6baefeea75674`, com alterações locais preexistentes da Fase 6 e documentação da Fase 7A. A implementação deve capturar novo baseline, preservando essas alterações. Conferências pontuais desta preparação:

- Não existe projeto KapiLab no checkout. `tools/BrandAssets` usa ProjectReference e não está na solução principal.
- SDK fixado em `10.0.400`; `net10.0`, analisadores e warnings como erros herdados; lockfile muda conforme `SlopOnnxBackend`.
- GenAI Cpu/WinML/Cuda permanece em `0.15.2`; `System.CommandLine` ainda não está no catálogo central.
- Registry expõe APIs públicas de descritores/schemas; isso não prova que o canal de sessão atual representa o catálogo autorizado do provider local.
- `LocalAgentProvider.SubmitToolResultAsync` ainda rejeita tool calling. A rota `agent --loop ide` depende das capacidades da Fase 7A.

Restore/build/test não foram executados nesta alteração documental. O baseline completo é F7B-00.

## Arquitetura e divisão de responsabilidade

Alvo da proposta ADR-071: `tools/KapiLab/KapiLab.csproj` e `tools/KapiLab.Tests/`, referências relativas a Core, Autocomplete.Core, LocalAi.Core, Application e adapters necessários. Organizar acesso à IDE em `Ide/`; código de JSONL, estatística, schemas, relatórios e workspace em `Core/`, sem copiar regras do produto. Não referenciar Desktop ou o executável de Benchmarks; não carregar configuração/persistência da aplicação pelo container global de DI.

Rota A, com fonte no repositório da IDE, é o alvo do planejamento. Rota B com DLLs externas é somente bootstrap opcional, identificando hash/commit/backend de cada build. Não manter dois produtos nem referências absolutas entre workspaces. IVT adicional e reflexão generalizada não são necessários ao MVP: preferir APIs públicas, gravador de `ILocalAiModelService` para capturar chat e decorador para métricas. Se faltarem descrições públicas, registrar a lacuna e usar uma ponte temporária estritamente limitada aos dois membros citados na fonte, com erro de drift, até a superfície compartilhada da 7A.

| Responsável | Entrega | Fronteira |
| --- | --- | --- |
| Fase 7A | Metadata, adapters, geração, provider local, segurança, exposição e superfícies públicas compartilhadas | Fonte de verdade do comportamento do produto |
| Fase 7B | CLI, fixtures, exportações, comparação, medição, replay e relatórios | Consome o comportamento real; registra caminhos experimentais separadamente |
| Workspace MongoIA/Python | Dataset de treino, exportação ONNX, Exec Match, task success, conversão de gates e fila externa | Integração por arquivos/CLI versionados; modificações externas ficam como entrega coordenada futura |
| Fase 10 | Aceite com pesos, hardware, sistemas operacionais e uso nativo | KapiLab oferece evidência auxiliar, não substitui a aplicação |

Os IDs K1–K4 do backlog de ajustes da Fase 7A são itens de catálogo/contratos/harness. Os marcos K0–K4 desta especificação são etapas do KapiLab; a coincidência dos nomes não implica equivalência. A rastreabilidade mantém os dois conjuntos identificados.

## Marcos e sequência

| Marco | Tarefas | Entrega e critério de saída | Dependências |
| --- | --- | --- | --- |
| K0 — fundação | F7B-00..03 | Baseline, projeto/lockfiles, CLI/formatos, isolamento de arquivos, `env`, inventário NPU. Contratos de saída/cancelamento e exclusão GPU definidos desde o começo | Checkout, proposta arquitetural, fixtures; inventário real depende do dispositivo |
| K1 — MVP | F7B-04..10 | `contract render/tokenize/diff`, `catalog export/check`, `model inspect/validate/run` autocomplete, `bench autocomplete`, relatório mínimo. Paridade de contrato, catálogo rastreável, medição do caminho real e gates sem falsos positivos | K0; pacote compatível/tokenizer para evidência real; catálogo local completo depende da 7A |
| K2 — avaliação ampliada | F7B-11..16 | Amostras determinísticas, validação estrita/hash, chat legado, quatro níveis de paridade, guidance e NPU smoke | MVP; pacotes/EPs; contrato candidato e NPU somente se adotados na 7A |
| K3 — agente | F7B-17..20 | Planos/invocações, laço experimental identificado, laço IDE integrado, replay e verificação externa | Catálogo seguro; provider/geração da 7A para `--loop ide`; fixtures e simulador versionados |
| K4 — integração e fechamento | F7B-21..24 | Matriz em subprocessos, fila/heartbeat, relatórios consolidados, wrappers/lock de build e runbook | Marcos aplicáveis; pipeline Python e hardware para homologação |

MVP = K0+K1. A ausência do Vivobook não impede contratos/CPU; registra inventário real pendente e bloqueia apenas o aceite NPU. O MVP pode extrair catálogos dos canais já existentes, identificados pelo provider, sem apresentá-los como catálogo local T4. Quando a 7A entregar o canal local, reexportar e invalidar o dataset/golden incompatível.

Sequência crítica: fundação → render/catalog/model → autocomplete/relatório → avaliação ampliada → agente integrado → consolidação. Inventário NPU e preparação de schemas podem avançar antes da geração do agente. Exclusão GPU, privacidade e identificação da rodada são requisitos do MVP, ainda que a integração completa da fila pertença a K4.

## Contratos da CLI e dos artefatos

Forma: `kapilab <grupo> <ação>`, identificadores/opções em inglês, ajuda e diagnósticos em pt-BR. Resolver workspace por `--workspace`, depois `KAPILAB_WORKSPACE`; sem ambos, informar uso inválido em vez de assumir o caminho pessoal da fonte. Resolução de pacote: argumento explícito, variáveis de compatibilidade documentadas ou catálogo de modelos; reportar a origem da seleção sem segredos.

JSON/JSONL UTF-8 sem BOM, dados em stdout e logs sanitizados em stderr. Versionar schemas; aceitar campos extras da entrada e validar estritamente campos consumidos, tipos, limites e IDs únicos. Preservar bytes dos prompts, inclusive CRLF/LF embutidos; não normalizar texto na comparação de contrato. Definir um único contrato de versão, resolvendo o conflito da fonte entre `schema_version` explícito e versão apenas no sufixo. A versão explícita deve concordar com o nome quando ambos existirem.

Artefatos por rodada: `env.json`, argumentos sanitizados, `metrics.json`, `per_example.jsonl`/`per_case.jsonl`, `gates.json` e relatório legível. Registrar commit e estado dirty da IDE, fingerprint da build, versão/hash do executável, backend/RID/configuração, GenAI/ORT, pacote e arquivos de contrato, tokenizer/template, provider solicitado/efetivo, fallback, seed, features do runtime e condições que desqualificam a medição. Rodada incompleta, abortada ou sobrescrita não aparece como sucesso; publicar resumo atomicamente ao concluir.

| Exit code | Contrato planejado |
| --- | --- |
| 0 | Operação concluída; não implica que todos os tiers ou gates externos foram homologados |
| 1 | Falha inesperada; erro sanitizado |
| 2 | Argumento/opção inválida |
| 3 | Entrada/schema/arquivo inválido |
| 4 | Pacote ausente, inválido, incompatível ou incompleto |
| 5 | Provider/hardware explícito indisponível ou falha de carga |
| 6 | Gate obrigatório reprovado, ausente ou rodada não qualificada para o gate solicitado |
| 7 | Drift de contrato/catálogo/build ou versão explicitamente esperada divergente |
| 8 | Violação de privacidade/conteúdo vedado |
| 9 | Recurso GPU ocupado ou exclusividade exigida não obtida |
| 10 | Falha nativa observada pelo supervisor de subprocesso |
| 11 | Divergência de contrato/paridade acima da tolerância |
| 12 | Cancelamento/timeout |

Erros específicos precedem agregação: carga explícita inválida retorna 5 mesmo sob `model validate --load`; `contract diff` e `parity` usam 11 para divergência, `catalog check` usa 7. Definir mapeamento de guidance indisponível antes de implementar: resultado estruturado `supported:false`, erro sanitizado e código 1 conforme a fonte; o chamador não pode confundi-lo com sucesso. Crash do processo raiz não consegue garantir código próprio: código 10 depende do supervisor. Documentar essas distinções nos wrappers.

## Critérios de aceite por capacidade

1. **Contratos:** `contract render` chama builders da IDE; chat legado é capturado pelo provider com serviço gravador, sem reimplementar seu prefixo privado. Vetores independentes cobrem bytes, escapes, truncagem, seleção/contexto e limites. Meta de paridade externa: 2.000 registros por contrato, 100% de texto e IDs quando aplicável; ausência de tokenizer marca comparação de IDs não executada.
2. **Tokenização:** usar adapter/tokenizer real e orçamento do runtime. Host menor somente com compatibilidade comprovada pelos hashes dos arquivos de tokenizer/configuração/tokens especiais, template aplicável e família; não inferir equivalência entre Qwen2 e Qwen3. Pacote incompatível falha; símbolos de token são obtidos do pacote, não hardcoded.
3. **Catálogo:** exportar descritores e schemas efetivamente disponíveis ao provider/modo/portas/grants. `approve` e nomes removidos ficam fora da exposição; lista negativa é separada. Não fixar teste em “12 descriptors/11 tools”: todas as tools vigentes aprovadas na ADR-070 continuam sujeitas às políticas reais. Hash canônico exclui apenas campos voláteis explicitamente definidos e não inclui o próprio hash; alterações de descrições, schemas, limites e planos causam drift verificável.
4. **Modelos:** inspeção/validação simples não carrega modelo nem gera texto; `--load` é explícito. Verificar integridade/contenção dos arquivos. `--strict-kapi` valida a especificação adicional e relata separadamente o estado aceito pela IDE; schema novo válido não prova suporte pelo produto. `runtime.minimumGenAi` é mínimo semântico, não igualdade exata. Equivalência de uma rodada exige comparação separada entre build da IDE, KapiLab e referência Python.
5. **Caminho de inferência:** catálogo → serviço compartilhado → runtime; ghost final vem de `AiAutocompleteProvider`, chat/agente do `LocalAgentProvider`. Decorador mede sem mudar texto/opções/fila. Self-check compara saídas equivalentes na mesma camada — raw com raw, ghost com ghost — e não exige igualdade entre raw e texto processado. Hardware explícito falha sem fallback; auto registra toda recuperação e desqualifica gate que exige o provider solicitado.
6. **Estatística:** aquecimentos identificados e excluídos; n, nulos, recusas, falhas, truncamentos e casos ignorados contabilizados. TTFT runtime/observado, tempo total, tok/s, memória e carga separados, com unidades e nearest-rank documentados. Memória/VRAM indisponível é `null` com motivo; nunca zero. Sem denominador válido ou amostra obrigatória suficiente, gate fica inconclusivo/reprovado, não aprovado.
7. **Amostras:** gerador reproduzível por seed/índice, com schemas sintéticos, gramática/cursor/sintaxe reais. Índice isolado reproduz a linha do lote. Saída `split:lab`, origem e execução `skipped`; nunca entra no treino ou conjunto cego. Fixtures declararem origem sintética não basta: validar manifesto/proveniência e conteúdo antes de exportar texto.
8. **Paridade:** tokenizer/prompt exigem equivalência exata; greedy segue tolerância e regime do arquivo de gates. Teacher forcing e laço direto GenAI recebem `path:genai-direct`; não contam como evidência da rota IDE. Não misturar normalização textual de concordância Python com a comparação byte a byte do contrato. Tolerâncias são dados versionados, não constantes oportunistas.
9. **Agente:** `--loop ide` depende dos contratos da 7A; protótipo `reference` permanece experimental, com fixtures/replay ou simulador, sem virar provider do produto ou autoridade sobre a política. Paridade verifica renderização/parser/resultados autorizados e tokens quando deterministicamente comparáveis; saídas estocásticas com seed não garantem equivalência entre EPs. Medir seleção/argumentos/hallucination, permissões, limites, loops, TTFA, orçamento e vazamento de canários conforme schema.
10. **Tools e verificação:** registry real com doubles exerce autorização/recusa/expiração/revogação/auditoria, inclusive caminhos de falha. Confirmações simuladas são específicas por chamada/caso e restritas às fixtures; ausência de aprovação nega. Porta Mongo opcional acessa apenas metadados de fixture loopback; documentos/diagnósticos são saídas sintéticas capturadas. Propostas/criação usam workspace descartável e controles existentes. Exec Match e task success são Python externo; sem resultado, `task_success:null` e gate obrigatório não passa.
11. **NPU/guidance:** inventário não baixa nem registra EP. `npu smoke --register-ep` é ação explícita de laboratório que pode precisar de rede/licença; sem ela o caminho é offline. Produzir `viable/not_viable/inconclusive` e motivos com EP/dispositivo efetivo. Hardware ausente ou bootstrap WinML falho não vira reprovação dos demais tiers. Pacote explicitamente NPU não cai silenciosamente em CPU; comparação CPU ocorre em outra execução identificada.
12. **Relatórios:** gates pertencem ao dono declarado, com valor, operador, limiar, evidência e qualificação. JSON deriva do `gates.yaml` externo e registra hash/versão. Não aprovar gate obrigatório sem evidência, sobrepor resultado Python ou misturar pacotes/builds/providers distintos em um agregado. `qualified:false` não produz aprovação final, ainda que um limiar isolado passe.

## Privacidade, arquivos e processos

Composição mínima sem perfis/cofre/LiteDB do usuário, sem download de modelos, publicação, telemetria ou autenticação externa. Filtrar segredos e marcadores em entradas/saídas não confiáveis; os marcadores FIM/ChatML montados pelos builders são estruturais e não devem ser bloqueados pelo mesmo filtro. Logs e traces padrão contêm contagens/hashes; predições sintéticas necessárias ao scorer ficam no artefato de dados. `--raw`/`--keep-text` exigem fixture verificada e opt-in, sem mudar a política de retenção do Desktop aprovada na ADR-070.

Normalizar e resolver caminhos antes de ler/escrever, incluindo `..`, links simbólicos/junctions, nomes de execução e raízes externas. BaseRepos, exports, contratos e fonte da IDE são somente leitura durante execução do laboratório. Saídas comuns ficam em `data/lab`, `reports/lab`, `Repos/<modelo>/reports/ide` e `tmp` da raiz explicitamente selecionada. Definir exceções por comando para catálogo em `data/agent/catalog`, inventário em `Repos/<modelo>/reports/npu`, resumo em `reports/ide_harness` e auditoria do conjunto cego. Não permitir `--out` arbitrário contornar a política. Wrapper de build publica executável e lock de distribuição; `report` emite proveniência, sem editar silenciosamente `model.lock.json`.

Conjunto cego é recusado por caminho e manifesto/hash; eventual modo explícito `--allow-blind --reason` pertence a avaliação controlada após o MVP, com registro de acesso. `samples` nunca usa essa exceção. `--skip-invalid` e descarte sensível preservam contagens e tornam a rodada inapta quando reduzem a cobertura obrigatória.

Exclusividade GPU deve existir antes da primeira carga, incluindo auto que seleciona GPU. Integrar token da fila ou lock exclusivo standalone, validar proprietário vivo/identidade da execução, respeitar pausa e liberar somente lock próprio. Tratar cancelamento, crash e lock órfão com teste; não apagar lock vivo por timeout. `--allow-gpu-parallel`, se implementado para diagnóstico, desqualifica gates. Um pacote DML por processo; matriz usa filhos sequenciais e registra crash/timeout sem perder resultados anteriores.

Comandos de verificação/simulador vêm de configuração explícita do operador, com executável e argumentos separados, timeout, diretório e ambiente controlados. Texto de dataset, resposta do modelo ou trace nunca vira comando de shell; substituir identificadores por argumentos, sem interpolação executável. URI de fixture só via variável dedicada, restrita a loopback, sem aparecer em argv/log/args.json. Nenhum gancho Python vira tool de consulta/escrita do agente.

## Decisões técnicas e conciliação da fonte

| Questão | Encaminhamento da meta |
| --- | --- |
| Sede A/B e D1 da fonte | Rota A proposta na ADR-071; bootstrap B opcional, nunca obrigatório para começar no repositório autorizado |
| D2 / B1 / B2 | Sem IVT novo no MVP; descrições compartilhadas pela 7A são preferidas à ponte de reflexão limitada; assinatura ausente gera drift |
| D3 / B10 | Reutilizar vocabulário de métricas; extração de biblioteca do harness é melhoria posterior, sem dependência no Exe Benchmarks |
| D4 | Schema estrito adicional versionado; quando a 7A implementar blocos, preferir o leitor real e teste de equivalência |
| D5 / B3 | Host pequeno verificado; API tokenizer sem pesos só se surgir necessidade comprovada |
| D6 | Gates JSON derivados de YAML pelo pipeline, sem parser YAML no console |
| D7 | Exec Match/task success externos; resultado ausente nunca equivale a sucesso |
| CLI / dependências | `System.CommandLine` é proposta; fixar versão/licença e escopo CPM no bootstrap. Herdar analisadores/lockfiles; não ampliar pacotes sem necessidade |
| Builds/plataformas | Cpu e WinML x64; WinML ARM64 condicionado à validação. Linux CPU tem gate próprio. Cuda documentado, sem promessa ou build/homologação sem equipamento |
| Privacidade/rede/caminhos | Exceções EP, catálogo, inventário e wrappers explícitas conforme seções anteriores; corrigem inconsistências de “sem rede” e “somente quatro pastas” da fonte |
| Fonte: referência como oráculo | Contratos reais e fixtures independentes arbitram divergência; protótipo não redefine comportamento/permissões da IDE |
| Fonte: limites/estimativa | Valores de versões, hardware e latência são hipóteses/alvos a validar. Resolver Q6 da 7A antes de adotar orçamento/gates T4 |

## Verificação planejada

Na implementação, executar os comandos oficiais sobre mudanças compartilhadas:

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Enquanto KapiLab estiver fora da solução, acrescentar restore/build/test explícitos dos projetos `tools/KapiLab` e `tools/KapiLab.Tests`, com `SlopOnnxBackend` correspondente e lockfiles Cpu/WinML. Esses projetos ainda não existem. O script de build deve verificar dependências e copiar nativos com proveniência, sem single-file inicialmente. Testes rápidos sem pesos podem ser automatizados; inferência real, benchmarks e medições continuam manuais, fora de CI/release. Testes das ferramentas Benchmarks existentes só com `EnableBenchmarkTests=true`.

Cobertura necessária: CLI/exit codes/schemas; goldens independentes; registro/decoração equivalentes; catálogo e drift por mutação; erros de pacote; seed/índice; estatística com vetores conhecidos; privacidade/path traversal; locks/cancelamento/crash/recovery; gate ausente/nulo/desqualificado; parsing e permissões adversariais. `KAPILAB_PACKAGE` habilita casos reais opt-in; sua ausência gera “não executado”, sem aceite por hardware. Para bloqueio da telemetria Avalonia, `-p:UsedAvaloniaProducts=` conforme AGENTS.md, sem desativar analisadores.

Artefatos de evidência do desenvolvimento em `artifacts/f7b/`, com comandos, commit/fingerprint, TRX, resultados de inspeção dos schemas, amostras sintéticas e limitações. Relatórios de execução do console seguem o workspace de laboratório. Alterações puramente CLI não exigem PNGs; qualquer alteração futura no Desktop passa pelo design system e pelos testes visuais reais.

## Estimativa e definição de pronto

A seção 10 da fonte estima K0 2–3, K1 7–8, K2 9–11, K3 10–12 e K4 3–4 pessoa-dia: soma **31–38**, com MVP **9–11**. O resumo externo cita 30–38; usar a soma como referência provisória, sem prazo de calendário. Recalibrar após baseline, composição do registry e primeiro pacote real; não duplicar esforço da 7A nem homologação da Fase 10. Datas/semanas relativas do plano externo não são agenda aprovada.

**Marco A — console e contratos:** tarefas aplicáveis implementadas, testes sem pesos aprovados, interfaces/formatos documentados, nenhuma regressão de segurança, resultados pendentes explicitamente identificados. Pacotes sem capacidade não ganham suporte por schema ou protótipo.

**Marco B — MVP real:** paridade de contrato em 2.000 registros, tokenizer real, catálogo fiel e benchmark legado com predições comparáveis ao Python, provider correto, nulos/falhas contabilizados e gates qualificados. A latência histórica ~381 ms da fonte é referência de ambiente antigo, não limiar universal.

**Marco C — integração K0–K4:** agente IDE confrontado com fixtures/replay, relatórios unidos por proveniência, wrappers operando e evidências reais dos tiers adotados; NPU/Cuda/Linux ausentes continuam pendentes ou formalmente retirados. Aprovação da ferramenta não aprova automaticamente os modelos nem encerra a Fase 10.

Ao concluir cada tarefa, preencher [rastreabilidade](rastreabilidade-f7b.md) com commit, teste/comando, evidência, resultado e limite. Atualizar plano, catálogo/inventário, acompanhamento, matriz e guia técnico; revisar ADR conforme decisões. Não marcar como implementado um item retirado ou um teste ignorado.
