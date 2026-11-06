# Validação e evidências

## Regra de aceite

Implementação e validação são dimensões distintas. Um requisito só fecha quando seus critérios aplicáveis têm evidência proporcional. Headless prova renderização/fluxo testado, não MongoDB real, modelo real, leitor de tela ou diálogo nativo. Ignorado, ambiente ausente e reexecução isolada não são aprovação integral.

Registrar: ID MVP e entrega da fase, commit/snapshot e alterações locais relevantes, data, SO/RID, versões do servidor/runtime/CLI/modelo/pacote quando aplicáveis, fixture, comando/procedimento, esperado, observado, resultado (aprovado/pendente/falhou/não aplicável justificado), artefatos sanitizados e limites. Não guardar credenciais/payloads sensíveis na evidência.

## Camadas

| Camada | Deve provar |
| --- | --- |
| Contratos/unitários | Limites, BSON, permissões, respostas obsoletas, erro e cancelamento por origem. |
| Integração local | DI/owner LiteDB, arquivos reais, migração, falha, concorrência, recuperação e processo quando necessário. |
| Renderização | PNGs reais inspecionados conforme design system, manifestando combinações e limitações. |
| Serviços/modelos reais | MongoDB descartável e runtime/modelo oficial: comportamento, permissões e qualidade no ambiente declarado. |
| Desktop nativo | Windows/Linux gráficos, teclado/IME, foco, leitor de tela, clipboard e diálogos. |
| Distribuição | Artefato por RID, instalar/atualizar/reabrir, falha/recuperação, dados preservados e licenças. |

## Comandos e política

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Em isolamento que bloqueie telemetria de build Avalonia, registrar uso de `-p:UsedAvaloniaProducts=`; analisadores/testes continuam ativos.

CI Windows/Linux, inclusive reutilizado pela release, roda somente UnitTests e Infrastructure.Agents.Tests. IntegrationTests e guardas estáticas de UnitSystemBoundaryIntegrationTests são validação manual antes de push/release.

CI nunca baixa/carrega modelos. Testes de Hub/pesos reais são Explicit e Category("LocalModelIntegration"), excluídos do CI; demais usam HttpMessageHandler falso. Executar localmente a guarda RealModelHubAccessIsConfinedToExplicitLocalModelTests do projeto IntegrationTests.

Benchmarks e latência/alocação são ferramentas manuais, nunca CI/release; ferramentas Benchmarks exigem EnableBenchmarkTests=true local. Definir CPU/pacote e orçamento antes de medir IA; registrar amostra, qualidade, tempo, memória, cancelamento e liberação sem extrapolar hardware.

## Baseline e regressões

Não copiar contagens históricas como resultado do checkout. Reusar testes existentes e adicionar casos significativos de comportamento/falha quando necessários. Não ajustar asserções/goldens para esconder regressões.

A intermitência LiteDB histórica precisa diagnóstico causal ou evidência equivalente antes do fechamento do cenário; sucesso serial não prova estabilidade concorrente. Aceites funcionais já dados permanecem válidos no seu alcance.

Matriz operacional: requisitos em [02](02-requisitos-e-estado.md), entregas em [fases](fases/README.md). Evidências históricas ficam em docs, inclusive [matriz anterior](../../docs/15-matriz-de-validacao.md) e [checklist](../../docs/16-checklist-homologacao.md); a nova baseline não declara esses cenários reexecutados.
