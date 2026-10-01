# Guia rápido de uso

**Copilot nos releases — 01/10/2026:** os novos pacotes incluem a CLI oficial interativa; **Entrar** abre seu login em terminal visível, e conta/chat compartilham a identidade oficial. Não é necessário instalar outra CLI no PATH. A pasta `runtimes/` deve acompanhar o executável ao extrair ou mover a instalação. [Correção e limites](architecture/release-copilot.md).

## Painel Agente IA — apresentação de 30/09/2026

O menu com o nome do provider permite escolher a integração; o selo Externo/Local indica o destino. Modelo e modo ficam junto da entrada. Expanda **Contexto da mensagem** para revisar os anexos e remover itens antes de enviar. **Anexar…** reúne arquivos do workspace e externos; **Permissões…** mantém as regras do provider.

**Enter** quebra linha e **Ctrl+Enter** envia. Durante a resposta, **Cancelar execução** fica disponível. Ao rolar para mensagens anteriores, a resposta não desloca a leitura: use **Ir para a última mensagem** para acompanhar novamente. Blocos de código preservam o texto e têm rolagem própria. Avisos críticos e falhas de histórico continuam visíveis; cancelar não promete desfazer operações já realizadas.


**Disponibilidade do agente — 30/09/2026:** ao iniciar, o aplicativo verifica em segundo plano o agente salvo quando o runtime permite a consulta sem diálogo. No Windows, Copilot reutiliza a autenticação da CLI oficial e carrega seus modelos elegíveis; não envia prompt nem abre conexão MongoDB. Se aparecer **Não verificado**, use **Verificar disponibilidade** ou **Testar conexão**. Quando for necessário login, use **Entrar** para iniciar o fluxo oficial; nenhum login interativo ocorre automaticamente. Consulte [P7-AUTH](phases/phase-07-v0.11.0/meta-inicializacao-autenticacao-agentes.md) para políticas e limites por agente/SO.

**Recuperação de sessão:** o snapshot respeita o opt-out geral e por conexão e exclui resultados de consultas. Falha ao salvar deve aparecer na interface; uma sessão ilegível não é substituída por uma sessão vazia. A [refatoração dos adapters](architecture/system-adapters.md) mantém essas regras antes da persistência e não altera os comandos de uso.

> **GitHub Copilot por assinatura — integração em andamento:** após verificar login oficial e modelos elegíveis, o chat com ferramentas mediadas pelo registry está disponível no Windows. O chip **Arquivo ativo** envia o texto não salvo da aba capturada naquele turno; removê-lo impede que `propose_file_edit` leia ou altere esse buffer. A proposta é revisada e aplicada ao editor com Undo, sem gravar em disco. Permissões por ferramenta, consentimento para envio de documentos MongoDB/diagnósticos e grants por conexão são controles separados; confirmação humana pode aparecer dinamicamente antes de uma chamada. **Permissões…** edita os opt-ins persistentes, que valem nos próximos turnos. Ferramentas nativas de shell, arquivos e rede e ferramentas de escrita MongoDB continuam desabilitadas. Consultas Copilot com MongoDB real, falhas/concorrência/recuperação no runtime oficial, diálogos nativos e leitor de tela ainda são gates pendentes. “Testar conexão” consulta conta e modelos sem enviar prompt/contexto ou iniciar inferência. A CLI oficial interativa acompanha o pacote; o login é feito por **Entrar** (comando oficial `copilot login`); não compartilhe códigos de dispositivo em mensagens. Ao apagar conversa, a exclusão da sessão nativa antecede a remoção LiteDB e, para opt-out, a limpeza do store volátil. Falha de exclusão ou histórico ilegível impede a limpeza e é reportada. Linux não será homologado nesta meta. Consulte [P7-COP](phases/phase-07-v0.11.0/README.md#meta-p7-cop--github-copilot-por-assinatura).

**Estado de validação em 29/09/2026:** o SDK oficial confirmou login `AuthType=user` e modelo elegível após o login concluído pelo usuário. A validação mais recente de ferramentas no registry usou somente perfil/pasta sintéticos; não acessou servidor MongoDB nem arquivos reais. Consulte a Fase 7 para evidências, limitações e próximos gates. Nenhuma homologação Linux está prevista para esta meta.

## O que está disponível na interface — 18/09/2026

A fase em execução é a [v0.6.0](phases/phase-02-v0.6.0/README.md). Alguns fluxos descritos adiante **continuam implementados no código, mas não têm entrada na interface** e por isso não podem ser executados pelos passos indicados:

| Fluxo | Estado | Onde está o requisito |
| --- | --- | --- |
| Botão **Ferramentas** (coleções, documentos, índices, administração, transferir, CRUD em lote, análise) | Sem entrada na interface | [Fase 6 / v0.10.0](phases/phase-06-v0.10.0/README.md) e [bkl-02](backlog/bkl-02-ferramentas-fora-de-fase.md) |
| Botão **Ambientes / Key Vault** | Sem entrada na interface. Os valores de ambiente continuam sendo resolvidos em tempo de execução | [bkl-01](backlog/bkl-01-key-vault-criptografico.md) |
| Modo **Script** (mongosh externo) | Desativado no seletor de modos | [bkl-03](backlog/bkl-03-script-engine-entre-conexoes.md) |
| Modo **Agregação** | Desativado no seletor de modos | [bkl-04](backlog/bkl-04-modo-aggregation.md) |

O seletor de modos deixou de ser exibido: **Console** é o único modo do editor. As seções abaixo que descrevem esses fluxos são mantidas como documentação do comportamento implementado, não como passos executáveis hoje.

## Idioma da interface

Use o seletor de idioma na barra superior para escolher **Português (Brasil)**, **English**, **Español** ou **简体中文**. A escolha é salva junto das preferências da sessão e é aplicada também às janelas abertas. `pt-BR` é o idioma inicial; quando a sessão contém um código inválido ou uma chave não possui tradução no idioma escolhido, o produto usa `en`. Se a chave também não existir em inglês, a interface mostra `[[chave]]` para tornar a lacuna observável.

Esta regra vale para a interface, mensagens, acessibilidade e exemplos gerados pelo produto. Os arquivos Markdown em `docs/` permanecem em português e não são traduzidos por esta meta.

## Consultas avançadas — incremento de 14/09/2026

Depois de um `$group` que cria `total`, Ctrl+Espaço oferece `$total` nas expressões seguintes. Projeções e `$unset` retiram campos das sugestões; `$lookup` distingue `localField` e `foreignField` quando existem resultados conhecidos da coleção relacionada. As sugestões acompanham os ramos de `$facet` e podem ser inseridas/desfeitas no editor sem execução. [Escopo validado e limites](backlog/27-consultas-avancadas.md).

O histórico agora inclui Agregação: reabra o pipeline em nova aba com destino e limite originais, sem executar. Falhas e cancelamentos também são registrados quando o histórico está habilitado. As sugestões de campos respeitam a coleção escrita no Console; expressões com destino dinâmico não reutilizam campos de outra coleção. Erros de comando mostram código MongoDB e orientação sem ecoar dados da resposta bruta.

Em **Opções → Validar sintaxe**, valide a seleção ou o editor inteiro sem conexão. Erros informam linha/coluna e selecionam o trecho. No modo **Agregação**, **Analisar pipeline (explain)** mostra o plano bruto estimado em Mensagens, no destino fixo da aba. `$out` e `$merge` são bloqueados no cursor de leitura. Ctrl+Espaço cobre os 12 stages da fase e predicados/acumuladores comuns. [Exemplos de uso, evidência e limites](backlog/27-consultas-avancadas.md).

## Disponibilidade e versões — 13/09/2026

A base atual é alpha (última tag local v0.1.1-alpha, com desenvolvimento posterior). Próximo alvo: **v0.5.0**, ainda 🚧 Em desenvolvimento. ✅ Explorer/consultas/CRUD/JSON já têm caminhos integrados. Abrir coleção prepara o texto; use Executar para carregar documentos. Exportar resultados oferece **Extended JSON da página carregada**, não CSV. 🚧 Formatação de apresentação JSON existe; comando geral de formatar query/script e CSV continuam pendentes do MVP.

Pipelines e ferramentas administrativas já disponíveis são antecipações técnicas: administração pertence à [v0.10.0](phases/phase-06-v0.10.0/README.md) e agregação está no [backlog](backlog/bkl-04-modo-aggregation.md). Console com múltiplas conexões e Script/mongosh estão no [backlog](backlog/bkl-03-script-engine-entre-conexoes.md). 🧪 Modelos locais/chat pertencem à v0.9.0, com limites de qualidade/hardware. A v1.0.0 é um marco futuro de estabilidade. Consulte [roadmap](09-plano-de-implementacao.md) e [inventário de código](24-inventario-roadmap.md) para limites e evidências; o restante deste guia descreve operações específicas, não aceite de release.

## Iniciar

Na raiz do projeto, execute:

```text
dotnet run --project src/EsilvaSoft.KapibaraStudio.Desktop
```

Abra **Conexões** na barra superior. Use **Nova conexão**, preencha nome/URI e, se desejar, **Preencher** a partir da URI. Revise ambiente, pasta, favoritos e autenticação e escolha **Salvar perfil**. Na lista, **Testar conexão** verifica o perfil e **Abrir conexão** carrega seus bancos no explorer. Editar, duplicar e remover operam sobre perfis locais; o resumo mostra apenas o host.

Expanda um banco para carregar coleções. A busca à esquerda considera somente itens já carregados. Enter ou duplo clique em uma coleção abre uma Consulta JSON sem executá-la. **Nova aba** cria Script; **Destino…** vincula a aba a uma conexão aberta/banco/coleção. Navegar no explorer não muda o destino de abas existentes.

**Sistema / Claro / Escuro** troca o tema. Em **… → Preferências**, ajuste fonte e recuperação de rascunhos. Abas recuperadas começam desconectadas e sem resultados. Desligar a recuperação elimina os snapshots correspondentes; salvar arquivos continua disponível.

## Credenciais diretas e ambientes opcionais

As duas conexões abaixo podem ser cadastradas simultaneamente. Variáveis de ambiente não são obrigatórias:

```text
mongodb://user:password@server/database
mongodb://user:${ENV.get("MONGO_PASSWORD")}@server/database
```

Uma URI direta é salva como foi configurada no perfil local. Os campos opcionais **Usuário** e **Senha na conexão** também permitem montar a URI, codificando os caracteres reservados. Deixe os dois campos vazios para preservar uma URI já preenchida, inclusive seus marcadores. Informar somente o usuário mantém o marcador legado `${MONGODB_PASSWORD}`; nenhum segredo existente é convertido automaticamente para senha direta.

O botão **Ambientes** foi removido da barra superior ([bkl-01](backlog/bkl-01-key-vault-criptografico.md)); a tela de gerenciamento descrita a seguir permanece implementada, sem ponto de entrada. Ela gerencia ambientes locais, **não** um cofre criptográfico: os valores são gravados sem criptografia. Development, Staging e Production estão disponíveis inicialmente; **Criar ambiente** adiciona nomes customizados. Selecione um ambiente, preencha chave/valor e use **Adicionar / atualizar variável**. A lista permite selecionar uma chave para editar ou remover. **Salvar e ativar ambiente** persiste todos os ambientes e ativa o selecionado. Fechar descarta mudanças ainda não salvas; selecionar na lista, por si só, não ativa o ambiente.

Cada ambiente possui seu próprio conjunto de textos. `ENV.get("chave")` consulta primeiro o ambiente ativo e, para compatibilidade, usa a variável do processo se a chave estiver ausente no ambiente. Uma chave ausente em ambas as origens gera erro; valores vazios cadastrados são válidos. Nomes de chave diferenciam maiúsculas/minúsculas no módulo.

- **URI:** `${ENV.get("MONGO_PASSWORD")}` interpola um componente, codificando caracteres reservados automaticamente. Cadastre o valor original, sem pré-codificar. Não use esse marcador para inserir uma URI inteira; configure a URI no perfil.
- **Script JavaScript:** `const password = ENV.get("MONGO_PASSWORD");` acessa o texto capturado para a execução. Pode ser combinado com lógica JS e valores BSON.
- **Consulta JSON e pipeline no editor:** `{ "tenant": ENV.get("TENANT") }` e `[{ "$match": { "tenant": ENV.get("TENANT") } }]` inserem um valor textual com escaping JSON. Chamadas dentro de strings entre aspas permanecem texto literal. Documentos BSON/Extended JSON não são convertidos em JSON simples.
- **Campos BSON dinâmicos:** filtros, documentos de alteração e pipelines que passam pelo adaptador usam a mesma chamada fora de aspas. Campos administrativos com validação JSON estrita mantêm essa validação; não são expressões JavaScript gerais. A entrada JSON de `slop.input` continua Extended JSON literal.
- **Legado:** `${MONGODB_PASSWORD}` e `${OUTRA_VARIAVEL}` continuam consultando o ambiente do processo, com valores já codificados para URI, como antes. As configurações existentes não são reescritas nem migradas.

A operação captura os valores antes do primeiro await. Trocar o ambiente não modifica uma execução em andamento. Após salvar ambientes, reabra as conexões para conferir o destino; abas e textos são preservados, mas o explorer anterior é invalidado. O campo **Rótulo de ambiente** do perfil é metadado de organização e não seleciona o ambiente do Key Vault.

Perfis com senha direta e variáveis do módulo são armazenados no workspace LiteDB local, sem criptografia por cofre nativo nesta entrega. O módulo não é o Key Vault de CSFLE/Queryable Encryption do MongoDB. Snapshots, histórico e auditoria não recebem automaticamente a URI resolvida nem os valores; texto sensível digitado ou impresso pelo usuário continua sujeito às opções de persistência existentes. O cofre nativo Windows/Linux permanece planejado.

O runner aceita ambas as URIs e transmite os valores pelo ambiente do processo filho `mongosh --norc --nodb`; a URI resolvida não entra nos argumentos nem no arquivo temporário de script. O bootstrap conecta antes de selecionar o banco da aba e disponibiliza `ENV.get()`. Homologação de autenticação e cancelamento com MongoDB/mongosh reais segue separada dos testes locais.

## Consulta e edição

1. Abra a conexão pela modal, expanda banco e abra coleção no explorer.
2. Para gerenciar a coleção da aba, a janela **Ferramentas → Coleções** (hoje sem entrada na interface) oferece: Para criar uma coleção capped, marque **Capped** e informe o máximo de bytes; o máximo de documentos é opcional. Para uma coleção clustered, marque **Clustered** e informe uma chave BSON de exatamente um campo, como `{ "_id": 1 }`; ela não pode ser combinada com view nem capped e depende da versão do servidor. O campo **Collation BSON opcional** aceita, por exemplo, `{ "locale": "pt", "strength": 1 }`. Para criar uma view, marque **View**, informe a coleção de origem e um pipeline JSON; views não aceitam opções capped. Para editar o pipeline da view selecionada, informe o novo array JSON, digite o nome exato da view e escolha **Atualizar view**. Para renomear a coleção selecionada, informe o novo nome e use **Renomear coleção**. A opção **Substituir destino** só deve ser marcada quando você deseja permitir que o servidor substitua uma coleção existente.
3. Para revisar ou atualizar um validador da coleção, comece por **Carregar validação**. A tela traz o documento, nível e ação existentes da coleção selecionada. Edite o documento JSON, como `{ "$jsonSchema": { "required": ["nome"] } }`; escolha nível e ação, digite exatamente o nome da coleção selecionada e use **Aplicar validação**. `Strict`/`Moderate` e `Error`/`Warn` são enviados ao MongoDB por `collMod`; use `Off` somente quando deseja desativar a aplicação da validação.
4. Para remover uma coleção, digite exatamente o nome da coleção selecionada no campo **Digite a coleção** e use **Remover coleção**. A ação não aceita nomes de sistema e não pode ser desfeita.
5. Use o modo **Console**: `db.getCollection("clientes").find({ status: "ativo" }).sort({ nome: 1 }).limit(100)`. Filtro e opções ficam no script; Ctrl+Espaço sugere APIs, conexões, bancos e coleções conforme o contexto. Veja a [API do Console](20-console.md).
6. O cabeçalho mostra conexão e banco; **Destino…** altera esse contexto explicitamente. A coleção é referenciada no próprio script. `getConnection()` e `ConnectionPool` permitem acessar outras conexões cadastradas durante a execução.
7. F5 executa o script completo; Ctrl+Enter executa a seleção ou o statement no cursor. Para explain, use a ferramenta existente **Consulta e schema** com seu filtro BSON; `explain()` não integra a API inicial do Console.
8. Para alimentar o autocomplete, **Amostrar campos** em Ferramentas → Consulta e schema (hoje sem entrada na interface). A IDE lê no máximo 200 documentos iniciais, infere apenas os caminhos BSON e mantém essa informação somente na memória da coleção selecionada. **Inferir validador** continua sendo uma ferramenta administrativa separada e preenche um `$jsonSchema` no editor de validação; não altera a consulta nem o servidor.
9. Contagens, valores distintos e paginação usam o texto e o contexto da consulta atual. Ações auxiliares podem aparecer nos resultados ou em ferramentas contextuais, mas não acrescentam campos obrigatórios ao editor.
10. Na aba Documentos, use **Carregar prévia** para revisar o primeiro documento correspondente em modo somente leitura antes de inserir, substituir ou fazer update parcial. **Duplicar prévia** prepara o conteúdo no editor de inserção e remove apenas o `_id` no nível superior; revise-o e escolha **Inserir** para criar a cópia. Updates exigem filtro não vazio; marque `Upsert` somente quando essa semântica for desejada. A atualização aceita um documento de operadores ou um pipeline JSON, por exemplo `[{ "$set": { "ativo": true } }]`. Para caminhos como `itens.$[item].ativo`, informe **arrayFilters JSON** como `[{ "item.status": "pendente" }]`. Use **Find-and-modify** quando precisar receber, na própria operação atômica, o documento depois da alteração; o resultado aparece no painel abaixo da prévia.
11. Para remover mais de um documento, marque **Excluir todos os correspondentes**. O filtro continua obrigatório e nunca é aceito vazio.

**Gerar identificador**, na aba Documentos das Ferramentas (hoje sem entrada na interface), obtém para obter um valor no modo de identificador e na representação UUID da conexão da aba: ObjectId gera `ObjectId("…")`, UUID v4 gera um UUID v4 no construtor configurado (por exemplo `JUUID("…")`) e Standard gera um de cada. A primeira caixa traz os construtores e a segunda o Extended JSON canônico com os mesmos valores. Em **Interpretar**, cole `ObjectId("…")`, 24 dígitos hexadecimais, `UUID("…")`/`CGUUID`/`JUUID`/`GUUID` ou um UUID com 32 dígitos para ver o tipo detectado, o Extended JSON canônico, o UUID equivalente de um ObjectId e o filtro por `_id`.

### UUID/GUID

Escreva o UUID com o construtor da origem dos dados; a escolha é explícita e vale mesmo em coleções mistas:

```javascript
db.Customers.find({ CustomerId: CGUUID("00112233-4455-6677-8899-AABBCCDDEEFF") }) // C# legacy, subtype 3
db.Customers.find({ CustomerId: JUUID("00112233-4455-6677-8899-aabbccddeeff") })  // Java legacy, subtype 3
db.Customers.find({ CustomerId: UUID("00112233-4455-6677-8899-aabbccddeeff") })   // Standard, subtype 4
db.Customers.find({ CustomerId: GUUID("00112233-4455-6677-8899-AABBCCDDEEFF") })  // Go/Standard, subtype 4
```

Os mesmos construtores funcionam em `EJSON.parse`, no modo Agregação, nos campos BSON das Ferramentas, em arquivos importados e no modo Script. Aceitam 32 dígitos hexadecimais com ou sem hífens; texto entre aspas continua string.

Em **… → Preferências**, **Representação padrão de identificadores** escolhe o modo — **Standard · ObjectId + UUID v4** (padrão), **ObjectId · MongoDB ObjectId** ou **UUID v4 · BSON subtype 4** — e a explicação e a prévia mudam com a seleção. Standard não é sinônimo de UUID: aceita e preserva os dois tipos. O modo decide o que é gerado (**Gerar identificador** e o `_id` de exemplo dos scripts CRUD do Explorer) e, em UUID v4, a árvore de Resultados mostra ao lado de cada ObjectId o **UUID equivalente** (12 bytes do ObjectId seguidos de 4 bytes zero). Essa forma é só apresentação: o JSON exibido, **Copiar JSON**, **Copiar _id**, **Copiar consulta por _id**, o editor e a exportação continuam com `ObjectId("…")`; use **Copiar UUID equivalente do _id** no menu do documento quando precisar do texto alternativo. Construtores explícitos são aceitos em todos os modos.

**Representação UUID · Binary BSON** define como a IDE exibe UUIDs; em **Conexões → Editar**, **Representação UUID desta conexão** sobrescreve a preferência ou usa a global. A prévia mostra o mesmo UUID nas quatro formas, com subtype e bytes. A escolha atualiza Resultados, Documentos, **Copiar**, **Abrir no editor** e o editor de documento; não altera dados gravados nem a exportação Extended JSON. Com Standard ou Go/Standard, subtype 3 aparece como binário legado de origem desconhecida até você escolher C# legacy ou Java legacy. Se a gravação da preferência falhar, a mensagem fica visível e a escolha vale somente nesta sessão.

Em **Histórico…**, habilite **Persistir histórico de consultas** para gravar o texto completo das consultas; a lista carregada limita-se às 50 entradas recentes. Selecione uma entrada e use **Carregar consulta do histórico** para reabrir o editor no contexto salvo. Consultas salvas podem ser criadas, atualizadas, marcadas como favoritas ou removidas nessa mesma janela. A substituição de conteúdo alterado pede confirmação.

**Anterior / Próxima** paginam a consulta da aba. **Exportar página…** abre o seletor de arquivo e grava Extended JSON; arquivos existentes não são sobrescritos. Uma tabela tabular ainda não faz parte desta revisão.

### Resultados: JSON e árvore

No cabeçalho da saída, escolha **JSON** ou **Árvore**. A escolha vale para a aba enquanto a IDE estiver aberta. **JSON** indenta os documentos sem converter tipos: `{"$numberLong":"…"}`, `{"$oid":"…"}` e datas continuam em Extended JSON, e UUIDs aparecem na representação da conexão. Clicar dentro de um documento o seleciona. **Árvore** lista os conjuntos do Console e seus documentos; expanda objetos e arrays para ver nome ou índice (`[0]`), tipo e valor. Avisos informam resultado vazio, limitado, projeção parcial, agregação ou JSON inválido. A seleção é mantida ao alternar.

**Copiar JSON** copia o documento selecionado já formatado. Botão direito, Shift+F10 ou a tecla Menu sobre um documento abrem:

- **Visualizar documento em JSON:** janela somente leitura com origem, conexão › banco › coleção, identidade e botão **Copiar JSON**; Esc fecha. Nada é executado.
- **Abrir documento para edição:** abre uma cópia editável sem ler nem gravar. **Salvar…** pede confirmação, relê o documento por `_id` e só grava se ele não mudou; documento alterado ou removido é informado e nada é gravado. Fica indisponível, com o motivo no menu, quando falta `_id`, a consulta usou projeção, o resultado veio de agregação ou script, ou o JSON é inválido. Em conexão somente leitura ou fechada, a cópia pode ser revisada, mas não salva.
- **Copiar JSON**; no modo JSON também **Copiar texto selecionado**.

A exportação continua em Extended JSON canônico. Resultados não são recuperados após reiniciar.

As chaves dos índices são BSON, por exemplo `{ "email": 1 }`. A tela também aceita índice único, esparso, oculto, TTL em segundos, filtro parcial BSON, como `{ "ativo": true }`, collation BSON, como `{ "locale": "pt", "strength": 1 }`, e **Projeção wildcard**. Esta última requer uma chave que inclua `"$**"`, por exemplo `{ "$**": 1 }`, e aceita inclusões ou exclusões de campos, como `{ "atributos.cor": 1 }`; não misture os dois modos, exceto no campo `_id`. Um índice oculto continua sendo mantido, mas o otimizador não o usa; confirme a compatibilidade da versão do servidor antes de criá-lo. Para remover um índice, informe o nome exibido na lista; o índice `_id_` é protegido.

Para alterar a visibilidade de um índice existente, informe e repita seu nome, marque **Oculto** quando necessário e use **Alterar visibilidade**. A operação usa `collMod`; o índice `_id_` permanece protegido.

Use **Uso dos índices** para carregar as estatísticas de acesso expostas pelo estágio `$indexStats`. O recurso é somente leitura e depende das permissões e da versão do servidor; o resultado não determina sozinho que um índice seja seguro para remoção.

Na aba **Agregações**, edite o pipeline no mesmo editor textual. Use **Sugerir estágios** para completar os estágios locais compatíveis, como `$match`, `$project`, `$group`, `$sort`, `$limit` e `$lookup`. Campos vêm da amostra ou dos resultados carregados da coleção atual e não são persistidos.

Operações demoradas exibem o estado no rodapé. Use **Cancelar operação** para solicitar cancelamento cooperativo; chamadas que já chegaram ao MongoDB não são desfeitas automaticamente.

## Arquivos de texto e workspace local — Fase 4 (disponível)

Use **Abrir** (`Ctrl+O`) para abrir qualquer arquivo de texto, inclusive vazio ou sem extensão. O conteúdo é carregado como texto e não é executado. **Salvar** (`Ctrl+S`) grava na origem; **Salvar como** (`Ctrl+Shift+S`) escolhe outro caminho e mantém a mesma aba. Se o arquivo mudou no disco enquanto estava aberto, escolha recarregar, sobrescrever explicitamente ou cancelar. Ao fechar uma aba alterada, escolha salvar, descartar ou cancelar.

**Abrir pasta** (`Ctrl+Shift+O`) seleciona uma raiz única e ativa o painel lateral **Arquivos**, ao lado da aba **Conexões**. Expanda a árvore para carregar pastas e arquivos. Enter ou duplo clique abre o arquivo; repetir a abertura ativa a aba já existente. **Novo arquivo**, **Nova pasta**, **Renomear** e **Excluir** ficam no painel e no menu contextual. A raiz não pode ser renomeada ou excluída, nomes duplicados são recusados e **Excluir** usa a lixeira do sistema. Trocar ou fechar a pasta preserva as abas abertas; um arquivo removido continua como documento sem caminho até ser salvo em outro local.

O workspace não executa consultas ao selecionar arquivos ou pastas. A recuperação pode guardar a raiz, o painel selecionado e documentos conforme as preferências; resultados e credenciais nunca entram no snapshot. A versão 2 da sessão migra a versão 1 de forma aditiva e mantém uma sessão ilegível protegida.

## Script JavaScript + JSON

O runtime usa `mongosh` instalado separadamente. Na aba de script, o JSON de entrada fica disponível em `slop.input` e os resultados estruturados podem ser enviados por `slop.results`:

```javascript
const filtro = slop.input.query;
const limite = slop.input.parameters.limite ?? 100;
const cursor = db.getCollection("clientes").find(filtro).limit(limite);
await slop.results.stream(cursor);
```

**Abrir** (Ctrl+O) carrega o arquivo em nova aba e nunca executa automaticamente. **Salvar** (Ctrl+S) grava o arquivo; **… → Salvar como…** (Ctrl+Shift+S) permite outro destino. Em **Histórico…**, abrir arquivo recente cria uma nova aba. **Persistir caminhos de arquivos** controla o histórico de caminhos.

Em **Opções**, o JSON fica disponível em `slop.input`; **Persistir entrada** é desmarcado por padrão. A recuperação de rascunhos salva também o texto do script, conforme a preferência global/por conexão, e não inclui resultados. O banco indicado em Destino inicializa `db`, sem alterar o banco de autenticação. Configure `SLOPDATAADMIN_MONGOSH_PATH` se necessário; o runner aceita credenciais diretas e referências opcionais conforme a seção de ambientes.

Atalhos adicionais: Ctrl+Tab/Ctrl+Shift+Tab alternam abas; Ctrl+W fecha; F6 alterna explorer/editor; Ctrl+Espaço solicita sugestões. Escape fecha a modal; no workspace cancela apenas a execução da aba ativa. Cancelar não reverte efeitos já enviados ao servidor.

## Transferência lógica

Em **Ferramentas → Operações e dados → Transferir** (hoje sem entrada na interface), **Exportar banco selecionado** cria uma pasta no diretório de dados do usuário com `manifest.json` e um arquivo Extended JSON por coleção. O limite é configurável por coleção. Para reimportar, informe a pasta do manifesto ou use **Escolher pasta**, selecione o banco de destino; a IDE faz upsert por `_id`, sem apagar dados ausentes ou executar `drop`.

Esse formato é intercâmbio de dados, não substitui `mongodump`/`mongorestore` em backup operacional.

## Modo somente leitura

Ao criar um perfil, marque **Somente leitura** para bloquear CRUD, updates, índices, importação, criação de coleção e scripts na camada de serviço. A permissão efetiva ainda depende do RBAC do servidor MongoDB.

## Diagnóstico

A aba Administração executa `serverStatus`, `hello`, `currentOp`, `usersInfo`, `rolesInfo`, `dbStats` e `collStats` da coleção selecionada quando o usuário possui os privilégios necessários. Para interromper uma operação, copie seu ID numérico exibido por **Operações correntes**, informe-o novamente para confirmar e use **Interromper operação**. A ação usa `killOp`, respeita o modo somente leitura e registra apenas o ID na auditoria local. Usuários e papéis também são consultas somente leitura. A aba Explain executa `executionStats`; consultas pesadas podem consumir recursos do servidor e devem ser usadas com limites adequados.

Use **Profiler atual** para consultar o nível e as opções correntes do profiler no banco selecionado. Essa ação usa `profile: -1` e não altera a coleta; a configuração do profiler continuará em fluxo administrativo próprio.

Para executar a verificação completa de uma coleção, selecione a coleção, repita seu nome no campo de confirmação e use **Validar integridade**. A ação envia `validate` com `full: true`, pode consumir recursos por tempo significativo e não está disponível em perfis somente leitura. A resposta bruta é exibida no painel e o histórico local registra apenas o alvo e o resultado resumido.

Use **Compactar coleção** somente após confirmar a janela de manutenção: repita o nome da coleção e marque **Forçar** apenas quando o seu runbook permitir. A ação executa `compact`; o comando pode estar indisponível, ou ter restrições, conforme versão e topologia do MongoDB. Ela não fica disponível em perfis somente leitura e registra apenas o alvo na auditoria local.

Use **Auditoria local** para listar ações administrativas registradas no workspace, incluindo criação, renomeação, validação e remoção de coleções, criação de usuário e remoção de banco. O registro guarda ação, data e alvo; não guarda URI de conexão, senha, papéis JSON nem conteúdo de documentos. Informe um caminho novo com extensão `.json` e use **Exportar auditoria** para salvar até 500 entradas; um arquivo existente não é sobrescrito.

Para criar um usuário, selecione o banco de destino, informe nome, senha e um array JSON de papéis, como `[{ "role": "readWrite", "db": "catalogo" }]`. Repita o nome no campo de confirmação. A senha é enviada apenas ao comando `createUser` e apagada após a operação; o recurso depende dos privilégios do usuário conectado. Para remover, informe o nome e repita-o no segundo campo; a ação usa `dropUser` e não pode ser desfeita. Para conceder ou revogar papéis, informe o mesmo formato JSON, escolha **Revogar** quando necessário e confirme o nome; a tela usa `grantRolesToUser` ou `revokeRolesFromUser`.

## Remoção de banco

Para criar um banco, informe **Novo banco**, uma **Coleção inicial** e repita o nome do banco. O MongoDB materializa o banco ao criar essa primeira coleção; por isso a tela não oferece banco vazio. Perfis somente leitura e os bancos internos `admin`, `config` e `local` são bloqueados.

No painel Administração, digite exatamente o banco selecionado antes de usar **Remover banco**. A IDE bloqueia `admin`, `config` e `local`; a remoção de banco não pode ser desfeita.


## Identidade e temas

A barra superior identifica **Kapibara Studio** pelo símbolo de recipiente com líquido azul/violeta; o título do sistema permanece EsilvaSoft.KapibaraStudio. Conexões, Nova aba, Abrir, Salvar e Ferramentas agora têm ícones acompanhados dos mesmos nomes e atalhos. Executar e Cancelar continuam explícitos por aba. Use Sistema, Claro ou Escuro no seletor de tema; a preferência continua imediata e persistida. Sem abas, a marca aparece com a ação Nova aba. Consulte as [prévias reais e o manual de identidade](18-identidade-visual.md).



## Database Explorer

Os perfis cadastrados aparecem no painel **Bancos**, mesmo desconectados. Expanda uma conexão para carregar seus bancos, depois um banco para carregar coleções; cada coleção oferece **Documentos** e **Índices**. Botão direito ou Shift+F10 abre ações do item. Atualização é granular e mantém expansão dos itens que ainda existem.

**Documentos** abre uma consulta sem executar. Execute pelo editor e navegue pela visualização **JSON** ou **Árvore** de **Resultados**; a aba **Documentos** continua oferecendo copiar, abrir em script, inserir, editar ou excluir. Edição/exclusão releem o documento completo e verificam se ele mudou antes da escrita. A página possui limites, filtro, sort e projection; a coleção não é carregada inteira. Resultados abertos no editor não são recuperados automaticamente como rascunhos; use Salvar para gravá-los explicitamente.

O painel **Detalhes do item** mostra estatísticas e definições. Em uma conexão, **Instâncias / topologia…** permite escolher um membro suportado ou voltar à seleção automática. A troca preserva o destino das abas anteriores e exige reabertura/revinculação explícita. **Gerar script CRUD** prepara comandos com banco/coleção corretos para revisão, sem executá-los.

Veja o [guia completo do Database Explorer](19-database-explorer.md), incluindo índices, DNS SRV, confirmações e limitações por topologia/permissão.
## Autocomplete opcional por IA local

Abra **… → Preferências → Autocomplete**. O padrão Automático usa IA quando um modelo externo compatível foi selecionado e pode ser carregado; nos demais casos usa sugestões básicas. Escolha Básico para não consumir memória de modelo. **Testar modelo** salva as opções e verifica geração de exemplo local. No editor, **Tab** aceita a prévia, **Esc** descarta e **Ctrl+Espaço** abre o menu. Nenhuma sugestão executa código. [Instalação manual, privacidade, hardware e troubleshooting](21-autocomplete-local.md).

### Autocomplete preditivo no cursor

Em **… → Preferências → Autocomplete**, informe o **Diretório de modelos** (vazio usa `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio\Models`; nesta máquina, por exemplo, `F:\models`) e escolha o **Modelo** na lista. Cada subpasta é um modelo; **Atualizar** reescaneia sem descarregar o modelo em uso, e pastas incompletas aparecem em "Pastas ignoradas". **Outra pasta…** usa um modelo fora do diretório. Em **Hardware**, Automático tenta NPU, GPU e CPU conforme o que foi detectado; CPU, GPU ou NPU explícitos não recorrem a outro hardware e explicam a falha. Mantenha contexto 2048, geração 32 e atraso 150 ms, salvo recomendação do próprio modelo. **Testar modelo** salva, recarrega e verifica pasta, tokenizer, sessão, provider e geração, com tempo de carga, primeiro token e tokens/s; a digitação normal solicita sugestões automaticamente, sem clicar nesse botão. A carga aparece na barra inferior. Sessões antigas com um caminho salvo continuam usando essa pasta como externa. [Catálogo, metadata e hardware](26-ia-local-multimodelo.md) e [SlopCoder-Mongo-1.5B-full](23-onnx-slopcoder.md#slopcoder-mongo-15b-full-qwen2--13092026).

Para instalar um SlopCoder-Mongo, use **Baixar modelo** na mesma janela. A lista reúne as versões ONNX publicadas no Hugging Face, com nomes no formato "família — hardware precisão" e, abaixo, tamanho e quando escolher:

| Família | Sem GPU | Com GPU (DirectML, Windows) |
|---|---|---|
| **SlopCoder-Mongo-0.5B** — leve, bom para autocomplete | CPU INT4 (~415 MB, padrão) ou CPU INT8 | GPU DirectML FP16 (padrão) ou GPU DirectML INT4 |
| **SlopCoder-Mongo-1.5B-full** — melhor no Agente IA local, exige mais memória | CPU INT8 (recomendado) ou CPU INT4 | GPU DirectML FP16 (recomendado) ou GPU DirectML INT4 |

Opções de GPU mostram "GPU não detectada nesta máquina" quando o runtime não encontrou uma. **Ver detalhes do modelo no Hugging Face** abre o card do modelo original, com formato de prompt, dados e limitações. Escolha a variante e clique em **Baixar**. O modelo vai para o diretório de modelos, na subpasta `<repositório>-<variante>` (por exemplo `SlopCoder-Mongo-1.5B-full-ONNX-dml-fp16`), e cada arquivo é conferido pelo hash publicado. **Abrir pasta**, ao lado de Procurar…, abre esse diretório no gerenciador de arquivos. Na lista **Modelo**, os modelos baixados aparecem com o mesmo nome e, abaixo, parâmetros, arquitetura e pasta. O progresso aparece na janela e na barra inferior, que também cancela; fechar a janela não interrompe o download. Ao concluir, o modelo fica selecionado: clique em **Salvar**. Variantes já instaladas aparecem como "instalado" e não são baixadas de novo; para substituir, remova a pasta. Um download cancelado retoma a partir dos arquivos já verificados.

O ghost text não integra o documento até aceitar. **Tab** avança uma parte; **Esc** descarta; digitar ou apagar recalcula. Desmarque **Aceitar sugestão em partes com Tab** para aceitar tudo. As opções de dicionário, Input, campos dos Resultados e contexto ampliado são independentes. O dicionário resolve nomes simples imediatamente; IA completa trechos maiores. Resultados contribuem somente com nomes de campos temporários da aba. Sem modelo válido, mantenha o dicionário ligado para continuar com sugestões básicas. [Detalhes](21-autocomplete-local.md).


## Cores de código MongoDB

Console, Script, Agregação, Input, resultados JSON e documentos destacam propriedades, valores, operadores, stages e tipos BSON automaticamente. Trocar Claro/Escuro atualiza abas e documentos abertos. Nomes de conexões, bancos, coleções e índices recebem cores próprias quando conhecidos no contexto; o destaque não valida nem executa código. Junto de um delimitador, o par é sublinhado; sem par, aparece a cor de erro. Tab continua aceitando ghost text e Escape descarta a sugestão. Em resultados extensos o destaque prioriza as linhas visíveis. [Tokens, arquitetura e limites](22-syntax-highlighting.md).

## ONNX SlopCoder e chat — 13/09/2026

Revisão implementada: [contrato, uso, distribuições CPU/WinML/CUDA e limites](23-onnx-slopcoder.md). DeepSeek-Coder FIM com tokenizer .NET e manifesto validado; chat e autocomplete compartilham sessão, preservando cancelamento e revisão das propostas. A exportação CPU fornecida foi executada em CPU; a tentativa DirectML falhou na geração e o fallback CPU foi validado. O modelo FIM pode acrescentar alterações não solicitadas no chat; fidelidade conversacional não está homologada.


## Datas BSON

Use `{ "CreatedAt": ISODate("2024-12-30T20:56:44.999Z") }` em documentos e `db.Customers.find({CreatedAt: {$gte: ISODate("2024-12-30T20:56:44.999Z")}})` no Console ou script. O `Z` identifica UTC e torna visíveis timezone, segundos e milissegundos. Entradas com offset, como `ISODate("2024-12-30T17:56:44.999-03:00")`, representam o mesmo instante e são exibidas normalizadas como `ISODate("2024-12-30T20:56:44.999Z")`; o BSON não conserva o fuso original. Resultados e cópias mostram o construtor ISODate; exportações usam Extended JSON. Datas BSON extremas permanecem na representação canônica para evitar perda.


## Formatação, exportação e status do MVP

- No editor, abra **Opções → Formatar JSON/query/script**. Uma seleção é formatada isoladamente; sem seleção, usa o conteúdo completo. O comando não consulta MongoDB. Ctrl+Z desfaz. Acima de 1 milhão de caracteres, selecione um trecho menor. Se editar durante a operação, o resultado obsoleto é descartado.
- **Exportar página…** permite Extended JSON ou CSV no seletor de arquivo. Exporta somente os documentos do conjunto/página carregados no momento do clique. Use arquivo novo: destinos existentes são preservados e geram erro. Cancelamento está na barra inferior; o arquivo parcial é removido.
- CSV usa cabeçalhos dos campos de primeiro nível em ordem de primeira ocorrência, vírgula, UTF-8, aspas escapadas e valores BSON/objetos/arrays como JSON canônico dentro da célula. Ausente fica vazio, string vazia fica entre aspas e null vira `null`. Strings/cabeçalhos com prefixo de fórmula recebem apóstrofo. CSV não promete round-trip BSON.
- A barra inferior mostra trabalho, percentual real quando conhecido, modo indeterminado nos demais casos e `+N operações`. Cancelar atua na operação em destaque; outras abas continuam. Cancelamento não reverte escritas já enviadas. Timeout aparece como tempo limite excedido, separado de cancelamento do usuário.
- A árvore mostra até 256 campos por grupo; expanda **Próximos campos…** para continuar. JSON e exportação continuam completos. Consultas permanecem limitadas no servidor; em páginas acima de 8 milhões de caracteres, reduza `limit` ou use projeção. Arquivos acima de 16 milhões de caracteres exigem um trecho menor.

Limites de homologação e medições: [auditoria do MVP](done/release_v0.5.0/25-auditoria-mvp-performance.md).

## Homologação nativa do MVP no Windows — 14/09/2026

O fluxo validado conecta o perfil `MvpNative`, expande databases sob demanda, executa a consulta somente após **Executar** e mostra o estado da operação na barra inferior. O seletor **Exportar página…** salvou Extended JSON e CSV usando o filtro escolhido ou a extensão digitada; CSV produz cabeçalhos e linhas com valores BSON convertidos para strings conforme a política documentada.

A validação visual Linux está dispensada. O clipboard nativo do Windows foi validado no fluxo de resultados. Exportações canceladas após escrita parcial removem o arquivo incompleto, conforme os testes de streaming. Uma medição inicial sem inspeção visual abriu a janela em 1.396 ms e registrou 220,3 MiB de working set após estabilização; leitor de tela ainda não faz parte da evidência desta máquina.

## Atualizações

Nos pacotes publicados (zip/tar.gz dos releases), o Kapibara Studio consulta exclusivamente [os releases de EsilvaSoft.KapibaraStudio](https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio/releases), pela API pública do mesmo repositório, 10 s após abrir e a cada 6 h. Havendo versão nova, aparece **Atualizar** à direita da barra superior (só o ícone em janelas estreitas; a dica mostra a versão). Clique para baixar: o progresso e **Cancelar** ficam na barra inferior. Ao concluir, o botão vira **Reiniciar**: escolha **Reiniciar agora** ou **Depois**. Em ambos os casos a nova versão é instalada quando o programa fecha, com as confirmações habituais de execuções e rascunhos.

- Instalação estável recebe apenas versões estáveis; instalação em pré-release recebe também pré-releases mais novas.
- O pacote só é instalado se o SHA-256 conferir com o publicado no release.
- Se a pasta do programa não permitir escrita, o botão abre a página do release para download manual.
- Falha na troca restaura os arquivos originais; a dica do botão mostra o motivo e uma nova tentativa ocorre no próximo fechamento.
- Execução pelo código-fonte (`dotnet run`) não se atualiza. Para desativar numa instalação, defina `SLOPSTUDIO_DISABLE_UPDATES=1`. [Decisão e limites](10-decisoes-arquiteturais.md).


## Revisão do plano de autocomplete — 15/09/2026

Disponível hoje: `Ctrl+Espaço` abre a lista tradicional (padrão de teclado desde o lote W0, 18/09/2026); `Ctrl+;` já é reconhecido, mas não insere `;` nem executa IA — só informa indisponibilidade; um override explícito salvo em `Ctrl+.` continua funcionando, sem ser mais o padrão; ghost básico/IA legados seguem como antes. Planejado: dois automáticos (preemptivo tradicional e IA) com flags independentes e aprendizado de estrutura de find em fundo com persistência. Não há nova opção acionável nesta revisão além da política de atalhos; consultar plano para comportamento futuro. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).

## Claude Code e Anthropic API

No Linux, a conclusão de Copilot/Claude está sendo acompanhada pela [meta P7-LINUX](phases/phase-07-v0.11.0/meta-linux-copilot-claude.md). Use a CLI oficial instalada e execute login nela pelo fluxo oficial. Copilot exige **Verificar disponibilidade**/**Testar conexão** explícito no Linux; a inicialização não deve abrir o cofre automaticamente. O pacote deve conter os launchers Copilot e o proxy MCP executáveis, mas isso não comprova homologação. As ferramentas MCP/aprovações Claude continuam indisponíveis na composição Linux até concluir o aceite nativo.

O chat com Claude Code e o fluxo oficial de login já estão disponíveis no código. A API Key Anthropic é uma modalidade separada, selecionada explicitamente. Para configurar, verificar conexão, entender ferramentas/permissões ou seguir o roteiro Windows, consulte [Claude no KapibaraStudio](phases/phase-07-v0.11.0/README.md). A homologação com conta Claude Pro real permanece pendente; não trate testes com mocks como validação da conta.
