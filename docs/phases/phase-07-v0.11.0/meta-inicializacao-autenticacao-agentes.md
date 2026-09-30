# Meta P7-AUTH — Inicialização automática e contratos de conta dos agentes

Data: **30/09/2026**. Estado: **planejada; análise concluída, implementação e homologação pendentes**. Escopo: EsilvaSoft.KapibaraStudio desktop .NET 10/Avalonia, MIT. Vinculada a ADV-09 e à Fase 7; não fecha os gates de P7-COP nem libera capacidades experimentais.

## Resultado esperado

Ao abrir a aplicação, verificar em segundo plano a conta oficial já autenticada do agente selecionado/restaurado, atualizar modelos e disponibilidade e permitir seu uso sem abrir Conexões/configurações nem clicar em Testar conexão. Ao selecionar outro agente, aplicar o mesmo contrato. Conta ausente ou expirada exige ação explícita **Entrar** no fluxo oficial do fornecedor.

“Login automático” significa reconhecer e reutilizar a autenticação mantida pelo runtime oficial. Não significa executar login interativo a cada abertura, guardar tokens no produto ou criar uma autenticação própria. Nenhuma checagem envia prompt, buffer, arquivos, contexto MongoDB ou executa inferência.

## Diagnóstico do checkout

| Evidência | Consequência |
| --- | --- |
| `Infrastructure.Agents/Copilot/CopilotSubscriptionAgentProvider.cs`: `_status` começa em `AgentProviderStatus.NotReported`; `GetStatusAsync` só devolve esse snapshot | Cada processo novo começa sem a conta/modelos verificados, mesmo com login válido na CLI |
| No mesmo provider, `CheckAccountAndModelsAsync` inicia cliente oficial, consulta `GetAuthStatusAsync`, aceita somente `AuthType=user` e então consulta modelos | Essa operação produz o estado necessário para habilitar o chat; não é um novo login |
| `Desktop/ViewModels/AgentChatViewModel.Conversations.cs`: `InitializeAsync` chama `StartAutomaticAvailabilityCheck`; `AgentChatViewModel.Permissions.cs` usa `Availability.CheckAsync` tanto automaticamente quanto em Tentar novamente | Já existe um gatilho automático e uma ação de recuperação no painel |
| `Desktop/Agents/AgentProviderAvailabilityService.cs` chama somente `IAgentProviderCatalog.RefreshProviderAsync`; `DesktopAgentProviderCatalog` chama `AgentProviderCatalog.GetStatusAsync` | A checagem automática chega ao snapshot passivo do Copilot e nunca executa a checagem de conta/modelos |
| `Desktop/ViewModels/AgentSettingsViewModel.cs`: Testar conexão chama a verificação CLI e depois atualiza o catálogo; `Desktop/App.axaml.cs`: `ClaudeCodeAccountManager.CheckAsync` roteia Copilot para `CheckCopilotAsync` → `CheckAccountAndModelsAsync` | Explica por que a ação manual habilita o agente |
| `Application/Agents/IAgentProvider.cs`, ADR-051/056/059 e documentação da Fase 7 distinguem catálogo/status passivos de checagem explícita | A correção deve adicionar uma operação ativa clara, preservando a leitura passiva |
| `Infrastructure.System/Copilot/CopilotRuntimeSettings.cs` configura o uso do usuário logado e filtra o ambiente do processo; `CopilotProviderIsolationTests.PassiveCatalogDoesNotProbeCliOrCreateSdkClient` exige ausência de probes passivos | Não alterar esse contrato nem reintroduzir autenticação por tokens ambientais |

**Causa identificada por análise estática:** a inicialização automática e o teste manual seguem caminhos diferentes. Apenas o segundo preenche o snapshot do Copilot. A captura enviada mostra “disponibilidade ainda não verificada”, compatível com esse caminho. Não há evidência nesta análise de que as credenciais oficiais tenham sido perdidas; também não foi executado login ou testada a conta real do usuário.

Os caminhos acima são relativos a `src/`, exceto o teste, em `tests/EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests/Copilot/`. O problema não depende de conexão MongoDB: autenticação do agente e abertura de banco são operações independentes.

## Arquitetura proposta

Concluir as abstrações existentes, mantendo `IAgentProvider`/`IAgentSession` como contratos da integração de chat, streaming, turnos e tools. Não criar outro provider nem substituir `IAgentRuntime`, `IAgentToolRegistry`, catálogo ou adapters de sistema.

Extrair o contrato de conta hoje definido em `Desktop/Agents/AgentCliAccountPorts.cs` para `Application/Agents`, com tipos neutros e sem referências Avalonia/SDK. Evoluir `IAgentCliAccountManager` para **`IAgentAccountManager`**, reutilizando o comportamento de Check/SignIn/SignOut; separar metadados de apresentação e escopo de leitura dos comandos de conta. O nome final pode ser ajustado na implementação, sem manter dois fluxos concorrentes. Providers API/local continuam nos contratos atuais de credenciais/status e não precisam implementar login CLI fictício.

Contrato de conta proposto:

```csharp
public interface IAgentAccountManager
{
    AgentAccountCheckPolicy DescribeCheckPolicy(string providerId);
    Task<AgentAccountSnapshot> CheckAsync(
        string providerId, CancellationToken cancellationToken);
    Task<AgentAccountCommandResult> SignInAsync(
        string providerId, CancellationToken cancellationToken);
    Task<AgentAccountCommandResult> SignOutAsync(
        string providerId, bool userConfirmedGlobalSignOut,
        CancellationToken cancellationToken);
}
```

Os tipos são propostas, ainda não presentes no código. `AgentAccountCheckPolicy` informa suporte à checagem automática, possibilidade de rede e interação com cofre/diálogos. `AgentAccountSnapshot` contém apenas classificação segura, motivo tipado e instante da checagem; não contém token, e-mail, saída bruta de processo ou diretório privado. Modelos/capacidades permanecem publicados no status/catalog existente, sem segunda lista autoritativa.

Registrar handlers/adapters de conta por provider em DI e resolvê-los por contrato, retirando o roteamento por marcas da classe aninhada em `App.axaml.cs`. `App` compõe; ViewModels não conhecem SDKs, processos nem classes concretas dos fornecedores. A adaptação de estado e regras permanece em Application/Agents ou Infrastructure.Agents, conforme dependências; processo, ambiente, descoberta, terminal e recursos nativos permanecem em Infrastructure.System (ADR-060).

Evoluir o serviço atual de disponibilidade para **`IAgentProviderInitializationService`** em Application, com implementação compartilhada. Reaproveitar cache, tempo limite e compartilhamento de operação por provider do `AgentProviderAvailabilityService`, evitando dois caches independentes. Ele coordena:

1. Capturar provider/modelo selecionados e política antes do primeiro `await`.
2. Para conta delegada elegível, chamar `IAgentAccountManager.CheckAsync`; para provider API/local, aplicar política de status existente sem provocar diálogo de cofre automático.
3. Atualizar somente a entrada correspondente do catálogo a partir do status produzido pelo adapter.
4. Publicar estado para painel e configurações. **Testar conexão**, **Tentar novamente**, startup e troca de provider usam esse mesmo serviço.

```text
Startup / seleção / Testar conexão / Tentar novamente
  → IAgentProviderInitializationService
    → IAgentAccountManager → handler oficial registrado
    → status existente → catálogo → apresentação

Entrar / Sair (ação explícita)
  → IAgentAccountManager → fluxo oficial → invalidar/reverificar estado

Enviar (ação explícita)
  → IAgentRuntime → IAgentProvider → IAgentSession → registry/permissões
```

`Describe`, `List` e `IAgentProvider.GetStatusAsync` permanecem passivos. A operação ativa de inicialização é documentada separadamente; rede de catálogo de modelos passa a ser permitida nessa operação, sem inferência. Preservar as homologações/limites de cada provider: autenticação não concede ferramentas, não abre banco e não remove gates experimentais.

## Ciclo de vida, falhas e concorrência

- Iniciar após composição e restauração das preferências/conversa, sem bloquear a primeira janela. Verificar o provider selecionado mesmo se o painel estiver recolhido; os demais somente quando selecionados. Não iniciar todos os runtimes na abertura.
- Uma checagem efetiva por provider; chamadores concorrentes compartilham a operação. Cancelar a espera de uma view não cancela a checagem usada por outra view. Encerramento cancela operações da aplicação e libera processos/clientes com prazo limitado.
- Usar `TimeProvider`, cache inicial de cinco minutos e prazo inicial de vinte segundos, reaproveitando os valores existentes como parâmetros sujeitos à validação. Cache expirado é rechecado quando necessário; não usar polling contínuo nem persistir “autenticado” como prova após reinício.
- Invalidar imediatamente após login, logout ou erro tipado de autenticação; a nova geração prevalece sobre respostas antigas. Login/logout e checagens concorrentes não podem publicar um snapshot anterior à mudança de conta. Trocar provider não permite que o retorno do anterior altere modelo, destino ou estado do novo.
- Distinguir não verificado, verificando, pronto, login necessário, método incompatível, CLI ausente, sem modelos, falha e timeout. Falha de rede/modelos não deve afirmar logout; autenticação válida e agente utilizável são dimensões distintas.
- Não repetir automaticamente um turno após erro de autenticação. Revalidar a conta e solicitar novo envio; nenhuma repetição de tools ou escrita pode ocorrer.
- Manter o modelo salvo se ainda elegível. Se removido do catálogo, apresentar escolha/aviso coerente sem reescrever conversas restauradas nem redirecionar sessões abertas. Retomada de conversa e autenticação da conta permanecem processos distintos.
- UI automática mostra Verificando e depois o estado/ação apropriada. Entrar só abre fluxo oficial por ação humana; logout global conserva a confirmação atual. Verificar se o runtime pode provocar diálogo nativo do cofre: quando não for possível garantir uma checagem não interativa, retornar ação necessária sem tentar contornar o cofre.
- Logs usam provider, código seguro, duração e origem da checagem, sem prompts, tokens ou saída bruta. Persistência continua no proprietário LiteDB existente, respeitando opt-outs e proteção de sessão ilegível; não é necessário armazenar segredos novos.

## Entregas e ordem

| Etapa | Entrega | Aceite |
| --- | --- | --- |
| AUTH-01 | Extrair/evoluir contrato de conta e registrar handlers em DI | ViewModels e coordenador sem casts para providers; nenhum fluxo paralelo; catálogo passivo intacto |
| AUTH-02 | Unificar inicialização, cache e publicação por provider | Startup, retry e teste manual chamam a mesma operação; falhas e respostas obsoletas isoladas |
| AUTH-03 | Adaptar Copilot primeiro, depois Claude/Codex conforme suporte atual | Conta oficial verificada sem prompt; sem mudança nas modalidades API nem gates experimentais |
| AUTH-04 | Integrar lifecycle Desktop e estados localizados | Agente selecionado fica pronto após startup com conta válida; aplicação continua utilizável durante falha/timeout |
| AUTH-05 | Testar falha/concorrência/recuperação e inspecionar renderizações | Evidências abaixo executadas; nenhuma credencial/resultado em snapshot |
| AUTH-06 | Atualizar ADRs, design system, guia, catálogo, matriz e andamento | Proposta torna-se comportamento documentado somente após evidência; P7-COP continua com gates próprios |

## Matriz de aceite a executar

| Cenário | Evidência exigida |
| --- | --- |
| Reabrir com Copilot selecionado e conta válida | Teste de composição startup → handler → catálogo → painel sem abrir configurações; homologação com CLI oficial no Windows |
| Conta ausente/expirada, CLI ausente, método diferente de assinatura, modelos vazios | Estados tipados, ação adequada, ausência de login interativo automático e de fallback API |
| Painel recolhido e preferências/conversa restauradas | Inicialização efetiva e estado consistente quando o painel for aberto; modelo válido preservado |
| Retry e Testar conexão simultâneos | Uma operação real, estado/catalog consistentes e nenhum probe passivo |
| Troca de provider, login/logout durante checagem, resultado após timeout | Resultado antigo descartado; nenhum estado/modelo novo sobrescrito |
| Cancelamento de uma view e encerramento da aplicação | Outra view mantém operação; encerramento libera cliente/processo; nenhum cancelamento compartilhado com abas |
| Falha de rede/SDK e recuperação | UI não bloqueia; retry efetivo; autenticado não confundido com pronto; nenhum turno repetido |
| Contratos passivos e limites de segurança | Doubles em memória comprovam zero login, prompt, sessão de chat, tools, MongoDB ou leitura de credenciais na checagem |
| API/local e Claude/Codex | Regressões dos contratos atuais, sem cofre inesperado, sem fallback e sem liberar capacidades não homologadas |
| Estados visuais e localização | Gerar e inspecionar PNGs reais nos temas/tamanhos/escalas do design system, incluindo textos longos; homologação nativa de teclado/leitor de tela separada |
| Windows/Linux | Contratos simulados em ambos; runtime oficial e diálogos em cada SO suportado. Linux permanece pendente até evidência específica, inclusive suporte Copilot atual |

Executar restore locked, build e testes conforme AGENTS.md; `-p:UsedAvaloniaProducts=` somente se a telemetria externa bloquear o ambiente. Testes unitários usam doubles das portas; SDK/CLI/processos/cofre reais ficam na integração explícita. Nesta meta documental não foram executados build, testes, login real ou renderizações, pois não houve alteração de runtime/UI.

## Práticas oficiais consultadas

- [GitHub — autenticação do Copilot SDK](https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/authenticate): aplicações desktop podem usar a conta já autenticada pela CLI; o SDK utiliza credenciais geridas pelo fornecedor. Aplicar o modo de usuário existente, sem OAuth próprio ou leitura de tokens. A documentação atual não comprova recursos novos no SDK 1.0.14 fixado; validar o contrato instalado antes de adotar APIs adicionais.
- [Claude Code — referência CLI](https://code.claude.com/docs/en/cli-reference): conservar comandos oficiais de autenticação/status e separação entre conta e turnos; não consultar arquivos de credenciais.
- [Microsoft — .NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host): trabalho em segundo plano deve ter proprietário e encerramento coordenado. Aplicar esse princípio ao lifecycle Avalonia existente; não introduzir Generic Host/IHostedService apenas para esta checagem, pois o Desktop hoje compõe `ServiceCollection` diretamente.

Estas fontes fundamentam a proposta; a causa acima foi identificada no código local. A meta fica concluída quando o planejamento e diagnóstico estão registrados; a entrega AUTH-01–06 só pode ser declarada implementada/homologada após os respectivos testes.
