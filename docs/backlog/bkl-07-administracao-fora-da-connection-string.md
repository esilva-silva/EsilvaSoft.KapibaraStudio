# Backlog — administração fora da connection string

**Origem:** recortes amplos de ADM-01/04/09 e dependências operacionais mencionadas na revisão da Fase 6. **Decisão do usuário — 08/10/2026:** a Fase 6 inclui somente operações cujo acesso ao servidor ocorre pela connection string MongoDB. Estes recortes ficam **sem fase/versão comprometida** (ADR-069).

## Itens retirados da Fase 6

| Recorte / rastreabilidade | Item de backlog | Acesso adicional necessário |
| --- | --- | --- |
| ADM-01 — observabilidade externa | Métricas de host não expostas por comandos MongoDB, leitura de arquivos de log/FTDC no servidor e serviços externos de monitoramento. | SO/filesystem remoto, agente de monitoramento ou API externa. Métricas retornadas por `serverStatus` e demais comandos permanecem na Fase 6. |
| ADM-04 — controle gerenciado | Gestão de usuários/papéis via plano de controle Atlas ou identidade externa. | API/console Atlas e credenciais administrativas próprias, ou provedor de identidade. Gestão por comandos MongoDB permanece na fase quando suportada. Recorte Atlas também rastreado por ADM-10. |
| ADM-09 — configuração persistente/inicialização | Editar `mongod.conf`/`mongos.conf`, opções somente de startup, persistir parâmetros na configuração do servidor e aplicar alterações que exijam reinício. | Arquivos/configuração do deployment e gerenciamento do processo/serviço. Consulta e alteração de parâmetros runtime por comandos suportados permanecem na fase. |
| ADM-09 — execução de manutenção no SO | Runbooks executáveis que parem/iniciem serviços, gerenciem processos, manipulem arquivos de dados, discos, permissões do SO ou executem manutenção offline. | Acesso administrativo ao host/SSH/orquestrador. Pré-requisitos, explicações de risco e diagnóstico do comando conectado continuam obrigatórios na Fase 6. |

## Itens já fora da fase, mantidos no backlog

- Provisionamento de cluster/nuvem, rede, alertas, snapshots/PITR e restauração gerenciada: ADM-10, [bkl-05](bkl-05-requisitos-sem-fase.md).
- Backup/restauração operacional com Database Tools, sincronização e agenda: TRF-04/06/07, bkl-05. Database Tools podem receber uma URI, mas sua integração exige executáveis e contratos próprios e já estava fora da Fase 6.
- Administração distribuída: ADM-05/06/07/08, bkl-05. Alguns comandos são executáveis pela conexão, mas isso não os inclui automaticamente na fase.

## Limite de classificação

Não mover um comando ao backlog apenas porque exige RBAC, versão/topologia específica, conexão direta a um nó ou não é permitido em determinada modalidade Atlas. A capacidade deve ser diagnosticada no alvo da connection string. `compact`, `validate`, `collMod`, `profile`, `currentOp`/`killOp` e comandos de usuários/papéis permanecem no recorte conectado quando suportados. Não usar API/SSH como fallback para comando recusado.

Exportação/importação lógica continua na Fase 6: o servidor é acessado pelo driver/conexão; arquivos locais, manifesto, conversão, relatório e checkpoint são processamento do Desktop, não acesso adicional ao host MongoDB. Materialização iniciada pelo usuário via pipeline também pode usar a conexão; automação externa não é requisito dessa entrega.

## Código, aceite e dependências

A movimentação é documental: preserva serviços, contratos, UI, DI, testes e evidências. Não concede acesso ao SO/API nem autoriza novos executores. Cada item exige futuro escopo explícito, canal de autenticação, confirmação, auditoria, matriz de capacidades e recuperação. Estes itens não bloqueiam o aceite da Fase 6; seus riscos não dispensam as proteções dos comandos que permanecem.

## Referências técnicas

- [Parâmetros runtime e persistência](https://www.mongodb.com/docs/v8.2/reference/command/setparameter/): o comando atua no servidor em execução; configuração persistente utiliza opção de inicialização/arquivo.
- [Capacidades de parâmetros](https://www.mongodb.com/docs/manual/reference/command/getparameter/): metadados distinguem alteração em runtime e startup.
- [Compact](https://www.mongodb.com/docs/manual/reference/command/compact/) e [profile](https://www.mongodb.com/docs/manual/reference/command/profile/): comandos MongoDB com limites de deployment/topologia/permissão.
- [API administrativa Atlas](https://www.mongodb.com/docs/atlas/configure-api-access/): plano REST e autenticação próprios, separados da conexão de dados.

[Fase 6](../phases/phase-06-v0.10.0/README.md) · [Revisão](../phases/phase-06-v0.10.0/revisao-2026-10-08.md) · [Catálogo](../03-catalogo-funcional.md) · [Segurança/administração](../07-dados-seguranca-e-administracao.md)
