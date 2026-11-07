# Guia de uso

## Conectar e navegar

Cadastre um perfil, escolha conexão e conecte. Confirme banco/destino no cabeçalho da aba. O Explorer lista bancos, coleções e índices. Abrir uma coleção prepara uma nova consulta; execute quando desejar. Selecionar outra coleção não redireciona uma aba aberta.

Use abas separadas para trabalhos independentes. O Console pode referenciar conexões cadastradas pelo contrato existente; confirme cada destino antes de uma escrita.

## Console e resultados

O Console é o modo principal. F5 executa o script inteiro; Ctrl+Enter executa seleção ou statement identificado pelo parser. Não há fallback para executar tudo quando a identificação falha. Variáveis vivem dentro da execução atual; inclua declarações necessárias na seleção.

```javascript
const orders = db.getCollection("Orders");
orders.find({ Status: "Completed" }).sort({ CreatedAt: -1 }).limit(100);
orders.aggregate([
  { $match: { Status: "Completed" } },
  { $group: { _id: "$CustomerId", Total: { $sum: "$Total" } } }
]);
```

O contrato é um subconjunto de JavaScript/MongoDB, não toda API mongosh. Cursores são limitados e materializados conforme execução; limit(0) não carrega tudo. Consulte Resultados, Mensagens e Erros da própria execução. JSON/Árvore preservam tipos suportados; exportar JSON/CSV exporta a página carregada.

Inserção, edição e exclusão exigem destino correto e proteções aplicáveis. Cópia projetada não substitui documento integral para editar. Cancelar interrompe a operação daquela aba quando possível, sem desfazer comandos já enviados.

## Arquivos, histórico e sessão

Ctrl+T cria aba; Ctrl+O abre; Ctrl+S salva; Ctrl+Shift+S salva como; Ctrl+Shift+O abre/troca a pasta da workspace. Ctrl+Tab/Ctrl+Shift+Tab alternam abas; Ctrl+W fecha respeitando alterações e execução. Histórico e consultas salvas permitem retomar texto, sem executar automaticamente.

Configure retenção de rascunhos conforme privacidade geral e da conexão; Input JSON exige opt-in. Erros de gravação/recuperação devem permanecer visíveis. Não descarte arquivo/sessão ilegível como se estivesse vazio.

Rascunhos antigos de Script/mongosh e Agregação abrem como Texto, com aviso de origem e execução desativada. O conteúdo permanece intacto. Em Opções, copie a entrada JSON recuperada ou controle sua persistência; use “Adaptar texto no Console (sem executar)” somente quando quiser preparar o conteúdo para o Console. Essa ação não converte o pipeline nem executa comandos. Arquivos JavaScript comuns continuam abrindo no Console; arquivos reabertos do histórico com entrada JSON antiga recebem recuperação explícita.

## Sugestões e agentes

Ctrl+Espaço abre autocomplete determinístico. Tab aceita ghost e Escape descarta. Ctrl+; solicita IA local explicitamente, se habilitada e com pacote compatível; revise antes de aceitar. Sem modelo, edição e autocomplete normal continuam disponíveis.

Abra Agente IA por Ctrl+Shift+A. Selecione provider/modelo disponível, contexto e permissões; envie pelo botão ou Ctrl+Enter no composer. Revise propostas e destino antes de aplicar. Consentimento de resultados/diagnósticos se refere a execução humana capturada: agente não consulta documentos nem escreve no MongoDB.

Copilot exige instalação oficial compatível e conta elegível. Caminho explícito inválido gera diagnóstico sem fallback. Opt-out de histórico local não promete apagar dados remotos nem histórico anterior.

## Gestão e transferência

Use jornadas disponíveis para bancos/coleções, índices, views e validadores, respeitando permissões e confirmações. Importação JSON/NDJSON/CSV e pacotes lógicos têm políticas de colisão e recuperação explícitas. Revise destino e relatório; pacote lógico não substitui backup operacional.

Atualização depende do modo instalado, conforme [distribuição](07-distribuicao-e-suporte.md). Limitações de modelo, provider, plataforma e homologação estão no [estado](02-requisitos-e-estado.md).

## BSON e UUID sem perda de tipo

Use construtores explícitos no Console e Extended JSON canônico nos arquivos/campos JSON. Texto que parece UUID continua string. Exemplo de filtro/valores (não executa por ser documentação):

```javascript
db.getCollection("Orders").find({
  _id: ObjectId("64b000000000000000000001"),
  CustomerId: UUID("00112233-4455-6677-8899-aabbccddeeff")
});
const values = {
  Count: NumberLong("9007199254740993"),
  Price: NumberDecimal("19.90"),
  CreatedAt: ISODate("2026-11-06T12:00:00.000Z")
};
```

NumberLong fora da precisão segura usa texto; não passar por Number JavaScript. UUID é Standard/subtype 4; CGUUID é C# legacy/subtype 3, JUUID é Java legacy/subtype 3, GUUID é alias Standard. A preferência de exibição não muda o significado do construtor. Legado de origem desconhecida permanece binário canônico; não adivinhar byte order. Valores inválidos devem falhar antes de acessar o servidor.

Exemplo Extended JSON preserva inteiro longo:
```json
{"Count":{"$numberLong":"9007199254740993"},"Price":{"$numberDecimal":"19.90"}}
```

## Passos de gestão e transferência

1. Conecte uma aba Mongo e confirme seu banco. Abra **Mais ações → Ferramentas do banco de dados**; escolha **Coleções**, **Índices** ou **Transferir** conforme tarefa.
2. Em Coleções, configure nome/opções ou view/validador e revise o alvo; confirmar remoção exige contexto explícito. Em Índices, revise chaves ordenadas e opções, crie/remova e releia o estado. Copiar script não executa a operação.
3. Em Transferir, **Exportar banco selecionado** gera pacote com manifest.json e arquivos Extended JSON por coleção; views/definições são registradas no manifesto. Revise limite por coleção e omissões. Isso difere de **Exportar página** JSON/CSV dos Resultados.
4. Para pacote, selecione a pasta do manifesto e o banco de destino, revise validação de arquivos/hashes/contagens e colisões. Reject é padrão e exige destino vazio. Upsert precisa confirmação explícita e só reutiliza coleções do pacote, sem views/namespaces extras.
5. Para arquivo avulso, use **Importar documentos**, selecione JSON array/NDJSON/CSV, database e coleção; revise prévia e mapeie colunas CSV para os tipos desejados.
6. Upsert substitui integralmente o documento com mesmo _id, removendo campos ausentes. Pode deixar escrita parcial; não há rollback. Import em destino populado não oferece checkpoint/reinício nem restauração de definições.
7. Se aparecer **Revisar importação interrompida**, carregue origem/opções novamente. Reinício elegível revalida fonte/plano e recomeça do documento zero somente com destino vazio; lote incerto não é pulado. Divergência ou destino não vazio exige revisão manual.

Profiler com novas capturas, killOp, validate/compact, parâmetros runtime e gestão de usuários/papéis foram retirados. Diagnóstico necessário às operações cotidianas permanece. Tickets antigos do profiler continuam acessíveis nas ferramentas: selecione o registro e a conexão de origem, confirme o banco e solicite a restauração explicitamente. Abrir a tela não altera o servidor; falhas e conflitos conservam o ticket. Operações humanas suportadas de schema são distintas das tools de agentes, que não amostram documentos.
