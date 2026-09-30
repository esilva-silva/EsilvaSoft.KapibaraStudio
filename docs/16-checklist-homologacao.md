# Checklist de homologação — Fase 9 / v0.13.0

Este checklist é o único critério de validação manual do roadmap. Ele concentra os cenários de ambiente, hardware e operação humana transferidos das fases funcionais; sua conclusão não altera retrospectivamente a evidência histórica das versões anteriores.

## Revisão UI/UX — evidência automatizada

- [x] Explorer de bancos e modal de conexões; abrir coleção não executa consulta.
- [x] Isolamento de abas, seleção sem fallback, cancelamento e somente leitura.
- [x] Persistência, recuperação, descarte e falha de gravação; opt-out geral/por conexão e opt-in de entrada.
- [x] Renderização dos dois temas em 960 × 620, 1366 × 768, 1920 × 1080 e escalas 100/150/200%.
- [x] Foco inicial da modal e Escape sem cancelar aba; alternância de abas e foco por teclado.
- [ ] Homologação com MongoDB/mongosh reais, leitores de tela, seletores de arquivo e gerenciadores de janelas nativos.

Veja [matriz de validação](15-matriz-de-validacao.md) para contagens e sistemas. Os itens abaixo conservam o checklist da homologação completa; não foram marcados por inferência a partir dos mocks.


Este checklist complementa a matriz de validação e separa evidência local de evidência que exige MongoDB e `mongosh` reais. Cada execução deve registrar data, sistema operacional, versão do servidor, versão do driver, perfil usado sem divulgar sua senha no relatório e resultado.

## Ambiente

- [ ] Windows com .NET 10 SDK instalado.
- [ ] Linux com .NET 10 SDK instalado.
- [ ] MongoDB acessível nos dois sistemas.
- [ ] `mongosh --version` registrado.
- [ ] Usuário de teste com privilégios mínimos e usuário separado para falhas de autorização.
- [ ] URI direta e URI com `${ENV.get("MONGO_PASSWORD")}` coexistem; Development/Production resolvem valores distintos após ativação e reabertura.
- [ ] Referências legadas preservadas após reinício; nenhum segredo migrado automaticamente.
- [ ] URI resolvida ausente de argumentos/arquivos temporários do runner e snapshots; credenciais diretas e variáveis ficam somente no armazenamento configurado.

## Fundação e LiteDB

- [ ] `dotnet restore --locked-mode`.
- [ ] `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -v:minimal`.
- [ ] `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -v:minimal`.
- [ ] Criar dois perfis, organizar por pasta, reiniciar a aplicação e confirmar a persistência no LiteDB.
- [ ] Confirmar que histórico, consultas salvas e auditoria não contêm URI resolvida, senha ou documentos completos.

## Conexão e consultas

- [ ] Testar `buildInfo`, `hello`, `serverStatus`, `currentOp`, `dbStats` e `collStats`.
- [ ] Escrever e executar no editor textual filtro, projeção, ordenação, `skip`, `hint`, `maxTimeMS`, comentário, `batchSize` e collation, confirmando que não há necessidade de campos auxiliares.
- [ ] Confirmar autocomplete contextual de operadores, campos, caminhos e estágios no editor e sua invalidação ao trocar a coleção.
- [ ] Executar Explain com `executionStats`.
- [ ] Conferir Extended JSON Canonical para ObjectId, data, Decimal128, Binary e UUID subtype 04.
- [ ] Validar autocomplete por amostra limitada sem persistir documentos.

## CRUD, índices e coleções

- [ ] Inserir, substituir, atualizar com operador, upsert, inserir em lote, excluir um e excluir em lote.
- [ ] Confirmar bloqueio de alterações em perfil somente leitura.
- [ ] Criar índice único, esparso, TTL, parcial e com collation; listar e remover índice não reservado.
- [ ] Confirmar proteção do índice `_id_`.
- [ ] Criar coleção comum, capped e view; editar pipeline de view e testar validador `$jsonSchema`.
- [ ] Renomear e remover coleção com confirmação exata.

## Transferência e scripts

- [ ] Exportar banco com UUIDs, datas, arrays e documentos acima do limite.
- [ ] Importar em banco de teste e confirmar upsert por `_id_` sem apagar destino.
- [ ] Executar script JavaScript com entrada JSON usando `mongosh --norc`.
- [ ] Confirmar `slop.input`, `slop.results.stream` e separação entre saída estruturada e console.
- [ ] Confirmar que abrir um arquivo `.js` nunca o executa automaticamente.

## Administração

- [ ] Listar usuários e papéis.
- [ ] Criar e remover usuário com confirmação, sem persistir senha.
- [ ] Conceder e revogar papéis com confirmação exata.
- [ ] Interromper operação com `killOp` após conferir novamente o ID numérico.
- [ ] Confirmar auditoria local para essas ações sem dados sensíveis.
- [ ] Verificar que o modo somente leitura bloqueia todas essas alterações.

## Evidência e encerramento

### Database Explorer

- [ ] Abrir duas conexões e expandir bancos, coleções e índices sem executar consultas de documentos automaticamente.
- [ ] Atualizar um ramo expandido e conferir preservação dos demais; desconectar durante uma resposta lenta e verificar descarte do retorno.
- [ ] Conferir hello/serverStatus, estatísticas de banco/coleção, validators e opções de índices com permissões reais.
- [ ] Em replica set descartável, alternar seleção automática e membro explícito; conferir destino das operações e contexto preservado nas abas antigas.
- [ ] Exercitar URI SRV com opções TXT, autenticação e TLS; verificar mensagens para arbiter, mongos e load balancer sem destino direto suportado.
- [ ] Consultar com filtro/sort/projection/limit e páginas; verificar UUIDs, Int64 e documentos aninhados no JSON, árvore e clipboard.
- [ ] Inserir, editar e excluir com confirmação; editar página projetada sem perder campos e provocar alteração concorrente antes da confirmação para verificar conflito.
- [ ] Criar/remover índice composto com opções e confirmar proteção de `_id_` e perfil somente leitura.
- [ ] Gerar scripts com nomes contendo espaços/aspas e executá-los manualmente na fixture; gerar nunca deve executar.
- [ ] Abrir resultado no editor, reiniciar e confirmar ausência do resultado nos rascunhos automáticos.
- [ ] Conferir menus pelo botão direito e Shift+F10, foco, confirmação e detalhes nos dois temas com leitor de tela.

### Encerramento

- [ ] Repetir os testes em Windows e Linux.
- [ ] Registrar falhas de privilégio como resultado esperado quando aplicável.
- [ ] Anexar logs sem credenciais e sem payloads sensíveis.
- [ ] Atualizar [12-acompanhamento-da-implementacao.md](12-acompanhamento-da-implementacao.md) e [15-matriz-de-validacao.md](15-matriz-de-validacao.md).
- [ ] Só marcar um item como homologado depois de executar o cenário correspondente contra MongoDB real.

## Console — complemento de 11/09/2026

- [x] Fixture Windows com dois processos MongoDB Community locais: três sintaxes, CRUD, aggregate, cursor, índices e perfis somente leitura.
- [x] Preservar ObjectId, UUID legado, Int64, Decimal128 e data no caminho real.
- [x] Testes controlados de ENV capturado, cancelamento por aba, histórico/opt-out, recuperação, autocomplete assíncrono e confirmação Avalonia.
- [ ] Repetir integração Console em Linux nativo.
- [ ] Validar autenticação/TLS e topologias distribuídas para Console.
- [ ] Homologar autocomplete/foco/confirmações com leitor de tela.

Esses itens não promovem as pendências históricas do runner mongosh a aprovadas. O Console usa outro runtime.


## Gates da Fase 9 por origem — revisão de 13/09/2026

Este checklist é de homologação, não declaração de inexistência do código. Consulte o [inventário](24-inventario-roadmap.md) e o [roadmap](09-plano-de-implementacao.md).

- [x] v0.5.0: CSV conforme contrato (aninhamento/escape/erros), formatador JSON/query/script e ciclo conectar → navegar → consultar → visualizar → editar → exportar homologados no Windows; validação visual Linux dispensada nesta meta.
- [x] v0.5.0: BSON, conflitos, contexto/cancelamento por aba e recuperação de sessão confirmados por testes/integração real; abrir coleção nunca executa consulta automaticamente.
- [ ] Origem v0.6.0: fixtures reais dos 12 stages prioritários; formatação, autocomplete contextual, execução parcial, erros e revisão de UX nos dois temas.
- [ ] Origem v0.7.0: índices e coleções com confirmação, privilégios, somente leitura, estatísticas e pós-condição verificadas no servidor.
- [ ] Backlog do modo script: automação entre dois servidores, BSON, limites, credenciais, falha após escrita e cancelamento; validar separadamente Console e mongosh quando houver versão comprometida.
- [ ] Origem v0.9.0: fidelidade das ações em pt-BR/en, fallback sem modelo, privacidade, obsolescência e hardware realmente executado.
- [ ] Origem v1.0.0: performance medida, instalação/atualização em máquinas limpas, acessibilidade nativa, segurança, avisos/SBOM e recuperação.


## Gate final do polimento MVP

- [x] JSON/CSV incremental da página com cancelamento, falhas e preservação do destino existente em testes automatizados.
- [x] Formatação sem execução e undo em controle real Headless.
- [x] Barra concorrente com prioridade, percentual real/indeterminado, cancelamento e troca de aba; PNGs de 18 combinações.
- [x] MongoDB 8.0.30 standalone local Windows: páginas, edição com conflito, BSON, exportação e cancelamento.
- [x] Repetir conectar → navegar → consultar → exportar em Windows gráfico; edição protegida coberta pela integração real.
- [x] Homologar pickers JSON/CSV em janela nativa do Windows, incluindo extensão digitada e conteúdo real.
- [x] Acionar cancelamento de consulta pela barra inferior e confirmar retorno ao estado pronto com mensagem de efeitos não revertidos.
- [x] Homologar clipboard nativo no Windows; o JSON copiado foi conferido sem URI ou credencial.
- [x] Validar cancelamento de exportação após escrita parcial, propagação do token e remoção do arquivo incompleto nos testes de streaming.
- [ ] Homologar leitor de tela por ferramenta nativa.
- [ ] Repetir o fluxo em Linux gráfico — validação visual dispensada pelo escopo desta meta.
- [x] Medir uma amostra de startup e CPU/RAM nativos sem inspeção visual (1.396 ms; 220,3 MiB; 3.734,4 ms CPU após 2 s).
- [ ] Repetir a medição em série e durante I/O demorado para obter perfil estatístico.

Evidência e limites em [25 — Auditoria](done/release_v0.5.0/25-auditoria-mvp-performance.md). Os gates históricos da v0.5.0 foram transferidos para esta Fase 9; arquivar a release não os fechou. Os itens de ambiente deste checklist referem-se ao armazenamento local não criptografado, não a um cofre: ver [bkl-01](backlog/bkl-01-key-vault-criptografico.md).

## Gates da v0.11.0 pré-release — escopo limitado, validação aberta

O código da Fase 7 já oferece Copilot por assinatura de forma limitada no Windows. O próximo pacote 0.11.x deve continuar marcado como **pré-release**, e a página [P7-COP](phases/phase-07-v0.11.0/README.md) é a fonte do comportamento presente, testes executados e limitações. Antes de publicar qualquer tag, executar o workflow `ci.yml`/`release.yml` no commit exato e inspecionar os quatro artefatos e checksums. Estes itens permanecem gates de homologação e impedem declarar suporte completo ou estável:

- [ ] Runtime oficial do Copilot chama ferramentas de leitura em MongoDB de teste com limites/Extended JSON, autorização escopada e sem expor segredos.
- [ ] Runtime oficial cobre negação, revogação, falha de auditoria, timeout, concorrência e recuperação sem repetir efeitos.
- [ ] Conta oficial, indisponibilidade e erro de autenticação não bloqueiam a IDE nem acionam fallback de API.
- [ ] Caixa de confirmação nativa e navegação por teclado/leitor de tela homologadas; renderização Headless é evidência complementar.
- [ ] Linux e cofre nativo Linux continuam gates de plataforma; a Fase 7 Copilot não declara homologação Linux.
- [ ] Contexto externo autorizado é observável; nenhum envio implícito; auditoria registra metadados sem secrets.
