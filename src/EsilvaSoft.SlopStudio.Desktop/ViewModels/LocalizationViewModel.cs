using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

/// <summary>
/// Catálogo de textos da interface. A resolução é determinística: idioma atual,
/// inglês e, por fim, um marcador explícito de chave ausente.
/// </summary>
public sealed partial class LocalizationViewModel : ObservableObject
{
    public static LocalizationViewModel Current { get; } = new();

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalog =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["language"] = "Language",
                ["theme"] = "Theme",
                ["connections"] = "Connections",
                ["newTab"] = "New tab",
                ["open"] = "Open",
                ["save"] = "Save",
                ["moreActions"] = "More actions",
                ["saveAs"] = "Save as…",
                ["preferences"] = "Preferences…",
                ["databases"] = "Databases",
                ["refreshSelectedNode"] = "Refresh selected node",
                ["searchLoadedItems"] = "Search loaded items",
                ["refreshDatabases"] = "Refresh databases",
                ["openManageConnections"] = "Open or manage connections",
                ["newScriptShortcut"] = "New script (Ctrl+T)",
                ["openShortcut"] = "Open file (Ctrl+O)",
                ["saveShortcut"] = "Save tab (Ctrl+S)",
                ["interfaceLanguage"] = "Interface language",
                ["connectionsTitle"] = "Connections",
                ["chooseOrigin"] = "Choose a source to explore its databases.",
                ["newConnection"] = "New connection",
                ["searchConnections"] = "Search name, host, folder or environment",
                ["noConnections"] = "No connection found. Adjust the search or create a connection.",
                ["edit"] = "Edit",
                ["duplicate"] = "Duplicate",
                ["removeProfile"] = "Remove profile…",
                ["removeLocalOnly"] = "Removes only the local profile.",
                ["fillFromUri"] = "Fill from a URI",
                ["quickUri"] = "Quick URI",
                ["fill"] = "Fill",
                ["name"] = "Name",
                ["defaultDatabase"] = "Default database",
                ["mongodbUri"] = "MongoDB URI",
                ["user"] = "User",
                ["passwordInConnection"] = "Password in connection",
                ["environmentLabel"] = "Environment label",
                ["folder"] = "Folder",
                ["environmentColor"] = "Environment color (#RRGGBB)",
                ["tags"] = "Tags",
                ["connectionUriNote"] = "The URI may contain direct credentials or ${ENV.get(\"MONGO_PASSWORD\")}. User/password fields are optional and save the password in the local URI. Without a password, the legacy marker ${MONGODB_PASSWORD} is used.",
                ["favorite"] = "Favorite",
                ["readOnly"] = "Read-only",
                ["openingConnection"] = "Opening connection…",
                ["testConnection"] = "Test connection",
                ["back"] = "Back",
                ["close"] = "Close",
                ["saveProfile"] = "Save profile",
                ["openConnection"] = "Open connection",
                ["removeLocalProfile"] = "Remove local profile",
                ["removeProfilePrompt"] = "Remove profile {0}? Databases will not be changed.",
                ["remove"] = "Remove",
                ["cancel"] = "Cancel",
                ["connectionName"] = "Connection name",
                ["connectionSearch"] = "Search connections",
                ["environment"] = "Environment",
                ["environmentFolder"] = "Folder",
                ["environmentVariableKey"] = "Key",
                ["environmentVariableValue"] = "Variable value",
                ["documentTitle"] = "Document",
                ["readOnlyView"] = "Read-only · opening this view does not execute a query or write data.",
                ["origin"] = "Origin",
                ["destination"] = "Connection › database › collection",
                ["identity"] = "Identity",
                ["presentation"] = "Presentation",
                ["formattedJsonReadOnly"] = "Formatted JSON document, read-only",
                ["copyJson"] = "Copy JSON",
                ["copyJsonTooltip"] = "Copies the displayed formatted JSON, with UUIDs in the connection representation",
                ["closeEsc"] = "Close (Esc)",
                ["cancelOperation"] = "Cancel operation",
                ["documentExtendedJson"] = "Extended JSON document"
                , ["environmentsTitle"] = "Local environments"
                , ["environmentNote"] = "Credentials in the URI are optional. Reuse values with ENV.get(\"key\")."
                , ["editActivateEnvironment"] = "Environment to edit and activate"
                , ["newEnvironmentName"] = "Name of the new environment"
                , ["createEnvironment"] = "Create environment"
                , ["environmentKeys"] = "Keys in this environment"
                , ["removeSelectedKey"] = "Remove selected key"
                , ["keyExample"] = "Key (e.g. MONGO_PASSWORD)"
                , ["variableValue"] = "Variable value"
                , ["addUpdateVariable"] = "Add / update variable"
                , ["noKeysNote"] = "No keys? Add the first variable. Each environment keeps its own values."
                , ["storageWarning"] = "Stored locally in the workspace without encryption: this is not a vault. Values are not included in draft snapshots. Closing discards unsaved changes."
                , ["saveActivateEnvironment"] = "Save and activate environment"
                , ["historyTitle"] = "History and saved queries"
                , ["consoleRuns"] = "Console and Aggregation — runs"
                , ["historyNote"] = "History stores the typed script, destination, environment, duration and status. It does not store results or resolved ENV values."
                , ["persistConsoleHistory"] = "Persist Console and Aggregation history"
                , ["consoleRunsAutomation"] = "Console and Aggregation runs"
                , ["openRunNewTab"] = "Open run in new tab"
                , ["connectionHistory"] = "History for this connection"
                , ["persistQueryHistory"] = "Persist query history"
                , ["loadHistoryQuery"] = "Load query from history"
                , ["savedQueries"] = "Saved queries"
                , ["loadSavedQuery"] = "Load saved query"
                , ["queryName"] = "Query name"
                , ["saveCurrentQuery"] = "Save current query"
                , ["newSavedQuery"] = "New saved query"
                , ["removeSavedQuery"] = "Remove saved query…"
                , ["removeSavedQueryPrompt"] = "Remove this query from local history?"
                , ["recentFiles"] = "Recent files"
                , ["persistFilePaths"] = "Persist file paths"
                , ["openRecentFileNewTab"] = "Open recent file in new tab"
                , ["refreshHistory"] = "Refresh history"
                , ["replaceContentTitle"] = "Replace content"
                , ["replaceContentPrompt"] = "Replace this tab's changed content with the selected query?"
                , ["replace"] = "Replace"
                , ["fileUnavailable"] = "File unavailable"
                , ["selectedModePreview"] = "Preview of selected mode"
                , ["objectIdPreview"] = "ObjectId preview"
                , ["uuidPreview"] = "UUID preview"
                , ["identifierInvariant"] = "The mode does not convert or rewrite data: ObjectId remains ObjectId and UUID remains Binary. Explicit constructors are accepted in every mode."
                , ["uuidFourForms"] = "Preview of the same UUID in four forms"
                , ["existingUuidHint"] = "Existing UUIDs continue to be shown in this representation. The comparison of all four forms appears in Standard and UUID v4 modes."
                , ["uuidInvariant"] = "Standard and Go/Standard show subtype 3 as legacy binary with unknown origin. The choice does not change stored data or canonical Extended JSON export; a UUID written as a string remains a string."
            },
            ["pt-BR"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["language"] = "Idioma",
                ["theme"] = "Tema",
                ["connections"] = "Conexões",
                ["newTab"] = "Nova aba",
                ["open"] = "Abrir",
                ["save"] = "Salvar",
                ["moreActions"] = "Mais ações",
                ["saveAs"] = "Salvar como…",
                ["preferences"] = "Preferências…",
                ["databases"] = "Bancos",
                ["refreshSelectedNode"] = "Atualizar nó selecionado",
                ["searchLoadedItems"] = "Buscar nos itens carregados",
                ["refreshDatabases"] = "Atualizar bancos",
                ["openManageConnections"] = "Abrir ou gerenciar conexões",
                ["newScriptShortcut"] = "Novo script (Ctrl+T)",
                ["openShortcut"] = "Abrir arquivo (Ctrl+O)",
                ["saveShortcut"] = "Salvar aba (Ctrl+S)",
                ["interfaceLanguage"] = "Idioma da interface",
                ["connectionsTitle"] = "Conexões",
                ["chooseOrigin"] = "Escolha uma origem para explorar seus bancos.",
                ["newConnection"] = "Nova conexão",
                ["searchConnections"] = "Buscar nome, host, pasta ou ambiente",
                ["noConnections"] = "Nenhuma conexão encontrada. Ajuste a busca ou crie uma conexão.",
                ["edit"] = "Editar",
                ["duplicate"] = "Duplicar",
                ["removeProfile"] = "Remover perfil…",
                ["removeLocalOnly"] = "Remove apenas o perfil local.",
                ["fillFromUri"] = "Preencher a partir de uma URI",
                ["quickUri"] = "URI rápida",
                ["fill"] = "Preencher",
                ["name"] = "Nome",
                ["defaultDatabase"] = "Banco padrão",
                ["mongodbUri"] = "URI MongoDB",
                ["user"] = "Usuário",
                ["passwordInConnection"] = "Senha na conexão",
                ["environmentLabel"] = "Rótulo de ambiente",
                ["folder"] = "Pasta",
                ["environmentColor"] = "Cor do ambiente (#RRGGBB)",
                ["tags"] = "Tags",
                ["connectionUriNote"] = "A URI pode conter credenciais diretas ou ${ENV.get(\"MONGO_PASSWORD\")}. Os campos usuário/senha são opcionais e gravam a senha na URI local. Sem senha preenchida, o usuário usa o marcador legado ${MONGODB_PASSWORD}.",
                ["favorite"] = "Favorita",
                ["readOnly"] = "Somente leitura",
                ["openingConnection"] = "Abrindo conexão…",
                ["testConnection"] = "Testar conexão",
                ["back"] = "Voltar",
                ["close"] = "Fechar",
                ["saveProfile"] = "Salvar perfil",
                ["openConnection"] = "Abrir conexão",
                ["removeLocalProfile"] = "Remover perfil local",
                ["removeProfilePrompt"] = "Remover o perfil {0}? Os bancos não serão alterados.",
                ["remove"] = "Remover",
                ["cancel"] = "Cancelar",
                ["connectionName"] = "Nome da conexão",
                ["connectionSearch"] = "Buscar conexões",
                ["environment"] = "Ambiente",
                ["environmentFolder"] = "Pasta",
                ["environmentVariableKey"] = "Chave",
                ["environmentVariableValue"] = "Valor da variável",
                ["documentTitle"] = "Documento",
                ["readOnlyView"] = "Somente leitura · abrir esta visualização não executa consulta nem gravação.",
                ["origin"] = "Origem",
                ["destination"] = "Conexão › banco › coleção",
                ["identity"] = "Identidade",
                ["presentation"] = "Apresentação",
                ["formattedJsonReadOnly"] = "Documento em JSON formatado, somente leitura",
                ["copyJson"] = "Copiar JSON",
                ["copyJsonTooltip"] = "Copia o JSON formatado exibido, com UUIDs na representação da conexão",
                ["closeEsc"] = "Fechar (Esc)",
                ["cancelOperation"] = "Cancelar operação",
                ["documentExtendedJson"] = "Documento Extended JSON"
                , ["environmentsTitle"] = "Ambientes locais"
                , ["environmentNote"] = "Credenciais na URI são opcionais. Reutilize valores com ENV.get(\"chave\")."
                , ["editActivateEnvironment"] = "Ambiente para editar e ativar"
                , ["newEnvironmentName"] = "Nome do novo ambiente"
                , ["createEnvironment"] = "Criar ambiente"
                , ["environmentKeys"] = "Chaves deste ambiente"
                , ["removeSelectedKey"] = "Remover chave selecionada"
                , ["keyExample"] = "Chave (ex.: MONGO_PASSWORD)"
                , ["variableValue"] = "Valor da variável"
                , ["addUpdateVariable"] = "Adicionar / atualizar variável"
                , ["noKeysNote"] = "Sem chaves? Adicione a primeira variável. Cada ambiente mantém seus próprios valores."
                , ["storageWarning"] = "Armazenamento local no workspace, sem criptografia: não é um cofre. Valores não entram nos snapshots de rascunhos. Fechar descarta alterações não salvas."
                , ["saveActivateEnvironment"] = "Salvar e ativar ambiente"
                , ["historyTitle"] = "Histórico e consultas salvas"
                , ["consoleRuns"] = "Console e Agregação — execuções"
                , ["historyNote"] = "O histórico guarda o script digitado, destino, ambiente, duração e status. Não guarda resultados nem valores resolvidos de ENV."
                , ["persistConsoleHistory"] = "Persistir histórico do Console e de Agregação"
                , ["consoleRunsAutomation"] = "Execuções do Console e de Agregação"
                , ["openRunNewTab"] = "Abrir execução em nova aba"
                , ["connectionHistory"] = "Histórico desta conexão"
                , ["persistQueryHistory"] = "Persistir histórico de consultas"
                , ["loadHistoryQuery"] = "Carregar consulta do histórico"
                , ["savedQueries"] = "Consultas salvas"
                , ["loadSavedQuery"] = "Carregar consulta salva"
                , ["queryName"] = "Nome da consulta"
                , ["saveCurrentQuery"] = "Salvar consulta atual"
                , ["newSavedQuery"] = "Nova consulta salva"
                , ["removeSavedQuery"] = "Remover consulta salva…"
                , ["removeSavedQueryPrompt"] = "Remover esta consulta do histórico local?"
                , ["recentFiles"] = "Arquivos recentes"
                , ["persistFilePaths"] = "Persistir caminhos de arquivos"
                , ["openRecentFileNewTab"] = "Abrir arquivo recente em nova aba"
                , ["refreshHistory"] = "Atualizar histórico"
                , ["replaceContentTitle"] = "Substituir conteúdo"
                , ["replaceContentPrompt"] = "Substituir o conteúdo alterado desta aba pela consulta escolhida?"
                , ["replace"] = "Substituir"
                , ["fileUnavailable"] = "Arquivo indisponível"
                , ["selectedModePreview"] = "Prévia do modo selecionado"
                , ["objectIdPreview"] = "Prévia de ObjectId"
                , ["uuidPreview"] = "Prévia de UUID"
                , ["identifierInvariant"] = "O modo não converte nem regrava dados: ObjectId continua ObjectId e UUID continua Binary. Construtores explícitos são aceitos em todos os modos."
                , ["uuidFourForms"] = "Prévia do mesmo UUID nas quatro formas"
                , ["existingUuidHint"] = "UUIDs existentes continuam exibidos nesta representação. A comparação das quatro formas aparece nos modos Standard e UUID v4."
                , ["uuidInvariant"] = "Standard e Go/Standard exibem subtype 3 como binário legado de origem desconhecida. A escolha não altera dados gravados nem a exportação Extended JSON canônica; UUID escrito como string continua string."
            },
            ["es"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["language"] = "Idioma",
                ["theme"] = "Tema",
                ["connections"] = "Conexiones",
                ["newTab"] = "Nueva pestaña",
                ["open"] = "Abrir",
                ["save"] = "Guardar",
                ["moreActions"] = "Más acciones",
                ["saveAs"] = "Guardar como…",
                ["preferences"] = "Preferencias…",
                ["databases"] = "Bases de datos",
                ["refreshSelectedNode"] = "Actualizar nodo seleccionado",
                ["searchLoadedItems"] = "Buscar en los elementos cargados",
                ["refreshDatabases"] = "Actualizar bases de datos",
                ["openManageConnections"] = "Abrir o administrar conexiones",
                ["newScriptShortcut"] = "Nuevo script (Ctrl+T)",
                ["openShortcut"] = "Abrir archivo (Ctrl+O)",
                ["saveShortcut"] = "Guardar pestaña (Ctrl+S)",
                ["interfaceLanguage"] = "Idioma de la interfaz",
                ["connectionsTitle"] = "Conexiones",
                ["chooseOrigin"] = "Elige un origen para explorar sus bases de datos.",
                ["newConnection"] = "Nueva conexión",
                ["searchConnections"] = "Buscar nombre, host, carpeta o entorno",
                ["noConnections"] = "No se encontró ninguna conexión. Ajusta la búsqueda o crea una conexión.",
                ["edit"] = "Editar",
                ["duplicate"] = "Duplicar",
                ["removeProfile"] = "Eliminar perfil…",
                ["removeLocalOnly"] = "Elimina solo el perfil local.",
                ["fillFromUri"] = "Completar desde una URI",
                ["quickUri"] = "URI rápida",
                ["fill"] = "Completar",
                ["name"] = "Nombre",
                ["defaultDatabase"] = "Base de datos predeterminada",
                ["mongodbUri"] = "URI de MongoDB",
                ["user"] = "Usuario",
                ["passwordInConnection"] = "Contraseña en la conexión",
                ["environmentLabel"] = "Etiqueta del entorno",
                ["folder"] = "Carpeta",
                ["environmentColor"] = "Color del entorno (#RRGGBB)",
                ["tags"] = "Etiquetas",
                ["connectionUriNote"] = "La URI puede contener credenciales directas o ${ENV.get(\"MONGO_PASSWORD\")}. Los campos de usuario/contraseña son opcionales y guardan la contraseña en la URI local. Sin contraseña se usa el marcador heredado ${MONGODB_PASSWORD}.",
                ["favorite"] = "Favorita",
                ["readOnly"] = "Solo lectura",
                ["openingConnection"] = "Abriendo conexión…",
                ["testConnection"] = "Probar conexión",
                ["back"] = "Volver",
                ["close"] = "Cerrar",
                ["saveProfile"] = "Guardar perfil",
                ["openConnection"] = "Abrir conexión",
                ["removeLocalProfile"] = "Eliminar perfil local",
                ["removeProfilePrompt"] = "¿Eliminar el perfil {0}? Las bases de datos no cambiarán.",
                ["remove"] = "Eliminar",
                ["cancel"] = "Cancelar",
                ["connectionName"] = "Nombre de la conexión",
                ["connectionSearch"] = "Buscar conexiones",
                ["environment"] = "Entorno",
                ["environmentFolder"] = "Carpeta",
                ["environmentVariableKey"] = "Clave",
                ["environmentVariableValue"] = "Valor de la variable",
                ["documentTitle"] = "Documento",
                ["readOnlyView"] = "Solo lectura · abrir esta vista no ejecuta consultas ni guarda datos.",
                ["origin"] = "Origen",
                ["destination"] = "Conexión › base de datos › colección",
                ["identity"] = "Identidad",
                ["presentation"] = "Presentación",
                ["formattedJsonReadOnly"] = "Documento JSON formateado, solo lectura",
                ["copyJson"] = "Copiar JSON",
                ["copyJsonTooltip"] = "Copia el JSON formateado mostrado, con UUID en la representación de la conexión",
                ["closeEsc"] = "Cerrar (Esc)",
                ["cancelOperation"] = "Cancelar operación",
                ["documentExtendedJson"] = "Documento Extended JSON"
                , ["environmentsTitle"] = "Entornos locales"
                , ["environmentNote"] = "Las credenciales en la URI son opcionales. Reutiliza valores con ENV.get(\"clave\")."
                , ["editActivateEnvironment"] = "Entorno para editar y activar"
                , ["newEnvironmentName"] = "Nombre del nuevo entorno"
                , ["createEnvironment"] = "Crear entorno"
                , ["environmentKeys"] = "Claves de este entorno"
                , ["removeSelectedKey"] = "Eliminar clave seleccionada"
                , ["keyExample"] = "Clave (p. ej., MONGO_PASSWORD)"
                , ["variableValue"] = "Valor de la variable"
                , ["addUpdateVariable"] = "Añadir / actualizar variable"
                , ["noKeysNote"] = "¿Sin claves? Añade la primera variable. Cada entorno conserva sus propios valores."
                , ["storageWarning"] = "Almacenamiento local en el espacio de trabajo, sin cifrado: no es una bóveda. Los valores no entran en las instantáneas de borradores. Cerrar descarta los cambios no guardados."
                , ["saveActivateEnvironment"] = "Guardar y activar entorno"
                , ["historyTitle"] = "Historial y consultas guardadas"
                , ["consoleRuns"] = "Consola y Agregación — ejecuciones"
                , ["historyNote"] = "El historial guarda el script escrito, destino, entorno, duración y estado. No guarda resultados ni valores resueltos de ENV."
                , ["persistConsoleHistory"] = "Guardar el historial de Consola y Agregación"
                , ["consoleRunsAutomation"] = "Ejecuciones de Consola y Agregación"
                , ["openRunNewTab"] = "Abrir ejecución en una pestaña nueva"
                , ["connectionHistory"] = "Historial de esta conexión"
                , ["persistQueryHistory"] = "Guardar historial de consultas"
                , ["loadHistoryQuery"] = "Cargar consulta del historial"
                , ["savedQueries"] = "Consultas guardadas"
                , ["loadSavedQuery"] = "Cargar consulta guardada"
                , ["queryName"] = "Nombre de la consulta"
                , ["saveCurrentQuery"] = "Guardar consulta actual"
                , ["newSavedQuery"] = "Nueva consulta guardada"
                , ["removeSavedQuery"] = "Eliminar consulta guardada…"
                , ["removeSavedQueryPrompt"] = "¿Eliminar esta consulta del historial local?"
                , ["recentFiles"] = "Archivos recientes"
                , ["persistFilePaths"] = "Guardar rutas de archivos"
                , ["openRecentFileNewTab"] = "Abrir archivo reciente en una pestaña nueva"
                , ["refreshHistory"] = "Actualizar historial"
                , ["replaceContentTitle"] = "Reemplazar contenido"
                , ["replaceContentPrompt"] = "¿Reemplazar el contenido modificado de esta pestaña por la consulta elegida?"
                , ["replace"] = "Reemplazar"
                , ["fileUnavailable"] = "Archivo no disponible"
                , ["selectedModePreview"] = "Vista previa del modo seleccionado"
                , ["objectIdPreview"] = "Vista previa de ObjectId"
                , ["uuidPreview"] = "Vista previa de UUID"
                , ["identifierInvariant"] = "El modo no convierte ni reescribe datos: ObjectId sigue siendo ObjectId y UUID sigue siendo Binary. Los constructores explícitos se aceptan en todos los modos."
                , ["uuidFourForms"] = "Vista previa del mismo UUID en cuatro formas"
                , ["existingUuidHint"] = "Los UUID existentes continúan mostrándose en esta representación. La comparación de las cuatro formas aparece en los modos Standard y UUID v4."
                , ["uuidInvariant"] = "Standard y Go/Standard muestran subtype 3 como binario heredado de origen desconocido. La elección no cambia los datos guardados ni la exportación Extended JSON canónica; un UUID escrito como cadena sigue siendo una cadena."
            },
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["language"] = "语言",
                ["theme"] = "主题",
                ["connections"] = "连接",
                ["newTab"] = "新标签页",
                ["open"] = "打开",
                ["save"] = "保存",
                ["moreActions"] = "更多操作",
                ["saveAs"] = "另存为…",
                ["preferences"] = "首选项…",
                ["databases"] = "数据库",
                ["refreshSelectedNode"] = "刷新选中的节点",
                ["searchLoadedItems"] = "搜索已加载的项目",
                ["refreshDatabases"] = "刷新数据库",
                ["openManageConnections"] = "打开或管理连接",
                ["newScriptShortcut"] = "新建脚本 (Ctrl+T)",
                ["openShortcut"] = "打开文件 (Ctrl+O)",
                ["saveShortcut"] = "保存标签页 (Ctrl+S)",
                ["interfaceLanguage"] = "界面语言",
                ["connectionsTitle"] = "连接",
                ["chooseOrigin"] = "选择一个来源以浏览其数据库。",
                ["newConnection"] = "新建连接",
                ["searchConnections"] = "搜索名称、主机、文件夹或环境",
                ["noConnections"] = "未找到连接。请调整搜索条件或创建连接。",
                ["edit"] = "编辑",
                ["duplicate"] = "复制",
                ["removeProfile"] = "删除配置…",
                ["removeLocalOnly"] = "仅删除本地配置。",
                ["fillFromUri"] = "从 URI 填写",
                ["quickUri"] = "快速 URI",
                ["fill"] = "填写",
                ["name"] = "名称",
                ["defaultDatabase"] = "默认数据库",
                ["mongodbUri"] = "MongoDB URI",
                ["user"] = "用户",
                ["passwordInConnection"] = "连接中的密码",
                ["environmentLabel"] = "环境标签",
                ["folder"] = "文件夹",
                ["environmentColor"] = "环境颜色 (#RRGGBB)",
                ["tags"] = "标签",
                ["connectionUriNote"] = "URI 可以包含直接凭据或 ${ENV.get(\"MONGO_PASSWORD\")}。用户和密码字段是可选的，密码会保存到本地 URI。未填写密码时使用旧标记 ${MONGODB_PASSWORD}。",
                ["favorite"] = "收藏",
                ["readOnly"] = "只读",
                ["openingConnection"] = "正在打开连接…",
                ["testConnection"] = "测试连接",
                ["back"] = "返回",
                ["close"] = "关闭",
                ["saveProfile"] = "保存配置",
                ["openConnection"] = "打开连接",
                ["removeLocalProfile"] = "删除本地配置",
                ["removeProfilePrompt"] = "删除配置 {0}？数据库不会受到更改。",
                ["remove"] = "删除",
                ["cancel"] = "取消",
                ["connectionName"] = "连接名称",
                ["connectionSearch"] = "搜索连接",
                ["environment"] = "环境",
                ["environmentFolder"] = "文件夹",
                ["environmentVariableKey"] = "键",
                ["environmentVariableValue"] = "变量值",
                ["documentTitle"] = "文档",
                ["readOnlyView"] = "只读 · 打开此视图不会执行查询或写入数据。",
                ["origin"] = "来源",
                ["destination"] = "连接 › 数据库 › 集合",
                ["identity"] = "标识",
                ["presentation"] = "表示",
                ["formattedJsonReadOnly"] = "格式化 JSON 文档，只读",
                ["copyJson"] = "复制 JSON",
                ["copyJsonTooltip"] = "复制显示的格式化 JSON，并使用连接的 UUID 表示形式",
                ["closeEsc"] = "关闭 (Esc)",
                ["cancelOperation"] = "取消操作",
                ["documentExtendedJson"] = "Extended JSON 文档"
                , ["environmentsTitle"] = "本地环境"
                , ["environmentNote"] = "URI 中的凭据是可选的。可以使用 ENV.get(\"键\") 重用值。"
                , ["editActivateEnvironment"] = "要编辑和激活的环境"
                , ["newEnvironmentName"] = "新环境名称"
                , ["createEnvironment"] = "创建环境"
                , ["environmentKeys"] = "此环境中的键"
                , ["removeSelectedKey"] = "删除选中的键"
                , ["keyExample"] = "键（例如 MONGO_PASSWORD）"
                , ["variableValue"] = "变量值"
                , ["addUpdateVariable"] = "添加 / 更新变量"
                , ["noKeysNote"] = "没有键？添加第一个变量。每个环境都保存自己的值。"
                , ["storageWarning"] = "无加密地存储在工作区本地：这不是保险库。值不会进入草稿快照。关闭窗口会丢弃未保存的更改。"
                , ["saveActivateEnvironment"] = "保存并激活环境"
                , ["historyTitle"] = "历史记录和已保存查询"
                , ["consoleRuns"] = "控制台和聚合 — 执行记录"
                , ["historyNote"] = "历史记录保存输入的脚本、目标、环境、耗时和状态。不保存结果或解析后的 ENV 值。"
                , ["persistConsoleHistory"] = "保存控制台和聚合历史记录"
                , ["consoleRunsAutomation"] = "控制台和聚合执行记录"
                , ["openRunNewTab"] = "在新标签页中打开执行记录"
                , ["connectionHistory"] = "此连接的历史记录"
                , ["persistQueryHistory"] = "保存查询历史记录"
                , ["loadHistoryQuery"] = "从历史记录加载查询"
                , ["savedQueries"] = "已保存的查询"
                , ["loadSavedQuery"] = "加载已保存的查询"
                , ["queryName"] = "查询名称"
                , ["saveCurrentQuery"] = "保存当前查询"
                , ["newSavedQuery"] = "新建已保存查询"
                , ["removeSavedQuery"] = "删除已保存查询…"
                , ["removeSavedQueryPrompt"] = "要从本地历史记录中删除此查询吗？"
                , ["recentFiles"] = "最近的文件"
                , ["persistFilePaths"] = "保存文件路径"
                , ["openRecentFileNewTab"] = "在新标签页中打开最近文件"
                , ["refreshHistory"] = "刷新历史记录"
                , ["replaceContentTitle"] = "替换内容"
                , ["replaceContentPrompt"] = "要用所选查询替换此标签页中已更改的内容吗？"
                , ["replace"] = "替换"
                , ["fileUnavailable"] = "文件不可用"
                , ["selectedModePreview"] = "所选模式预览"
                , ["objectIdPreview"] = "ObjectId 预览"
                , ["uuidPreview"] = "UUID 预览"
                , ["identifierInvariant"] = "该模式不会转换或重写数据：ObjectId 仍为 ObjectId，UUID 仍为 Binary。所有模式都接受显式构造函数。"
                , ["uuidFourForms"] = "同一 UUID 的四种形式预览"
                , ["existingUuidHint"] = "现有 UUID 仍以此表示形式显示。Standard 和 UUID v4 模式会显示四种形式的比较。"
                , ["uuidInvariant"] = "Standard 和 Go/Standard 将 subtype 3 显示为来源未知的旧版二进制。此选择不会更改已写入的数据或规范 Extended JSON 导出；以字符串写入的 UUID 仍是字符串。"
            }
        };

    private static readonly (string Key, string En, string Pt, string Es, string Zh)[] WorkspaceToolsTranslations =
    [
        ("cappedCollection", "Capped", "Capped", "Capped", "Capped"),
        ("viewCollection", "View", "View", "Vista", "视图"),
        ("clusteredCollection", "Clustered", "Clustered", "Clustered", "Clustered"),
        ("toolsQuerySchema", "Query and schema", "Consulta e schema", "Consulta y esquema", "查询和模式"),
        ("toolsAnalysis", "Analysis", "Análise", "Análisis", "分析"),
        ("toolsDistinctValues", "Distinct values", "Valores distintos", "Valores distintos", "不同值"),
        ("toolsExplain", "Explain", "Explain", "Explain", "Explain"),
        ("toolsAggregations", "Aggregations", "Agregações", "Agregaciones", "聚合"),
        ("toolsOperationsData", "Operations and data", "Operações e dados", "Operaciones y datos", "操作和数据"),
        ("toolsTransfer", "Transfer", "Transferir", "Transferir", "传输"),
        ("toolsAdministration", "Administration", "Administração", "Administración", "管理"),
        ("toolsDocuments", "Documents", "Documentos", "Documentos", "文档"),
        ("toolsBulkCrud", "Bulk CRUD", "CRUD em lote", "CRUD por lotes", "批量 CRUD"),
        ("toolsIndexes", "Indexes", "Índices", "Índices", "索引"),
        ("toolsCollections", "Collections", "Coleções", "Colecciones", "集合"),
        ("filterExtendedJson", "Extended JSON filter", "Filtro Extended JSON", "Filtro Extended JSON", "Extended JSON 过滤器"),
        ("explainExecutionStats", "Explain (executionStats)", "Explain (executionStats)", "Explain (executionStats)", "Explain (executionStats)"),
        ("exactCountButton", "Exact count", "Contagem exata", "Conteo exacto", "精确计数"),
        ("estimateCollection", "Estimate collection", "Estimar coleção", "Estimar colección", "估算集合"),
        ("schemaSampleDocuments", "Documents in schema sample", "Documentos na amostra de schema", "Documentos en la muestra del esquema", "模式样本中的文档"),
        ("sampleFields", "Sample fields", "Amostrar campos", "Muestrear campos", "采样字段"),
        ("inferValidator", "Infer validator", "Inferir validador", "Inferir validador", "推断验证器"),
        ("suggestMql", "Suggest MQL", "Sugerir MQL", "Sugerir MQL", "建议 MQL"),
        ("applySuggestion", "Apply suggestion", "Aplicar sugestão", "Aplicar sugerencia", "应用建议"),
        ("inferredValidatorNote", "Inferred validator: review before applying it in Collections.", "Validador inferido: revise antes de aplicar na ferramenta Coleções.", "Validador inferido: revise antes de aplicarlo en Colecciones.", "推断的验证器：应用到集合工具前请先检查。"),
        ("field", "Field:", "Campo:", "Campo:", "字段："),
        ("maximum", "Maximum:", "Máximo:", "Máximo:", "最大值："),
        ("distinctSearch", "Find distinct values", "Buscar valores distintos", "Buscar valores distintos", "查找不同值"),
        ("distinctNote", "Runs $match, $group and $limit on the server with the current filter; the limit is applied before values are transferred.", "Executa $match, $group e $limit no servidor com o filtro atual; o limite é aplicado antes de transferir os valores.", "Ejecuta $match, $group y $limit en el servidor con el filtro actual; el límite se aplica antes de transferir los valores.", "使用当前过滤器在服务器上执行 $match、$group 和 $limit；传输值之前会应用限制。"),
        ("explainPlanReadOnly", "Query plan and statistics (read-only command)", "Plano e estatísticas da consulta (comando somente leitura)", "Plan y estadísticas de la consulta (comando de solo lectura)", "查询计划和统计信息（只读命令）"),
        ("distinctFieldPlaceholder", "E.g.: status or address.city", "Ex.: status ou endereco.cidade", "P. ej.: status o direccion.ciudad", "例如：status 或 address.city"),
        ("limit", "Limit:", "Limite:", "Límite:", "限制："),
        ("suggestStages", "Suggest stages", "Sugerir estágios", "Sugerir etapas", "建议阶段"),
        ("executePipeline", "Execute pipeline", "Executar pipeline", "Ejecutar pipeline", "执行管道"),
        ("pipelineJsonExample", "Pipeline JSON, for example: [{ \"$match\": { \"active\": true } }]", "Pipeline JSON, por exemplo: [{ \"$match\": { \"ativo\": true } }]", "Pipeline JSON, por ejemplo: [{ \"activo\": true }]", "管道 JSON，例如：[{ \"ativo\": true }]"),
        ("pipelineResult", "Pipeline result", "Resultado do pipeline", "Resultado del pipeline", "管道结果"),
        ("exportNote", "Export creates a folder with a manifest and one Extended JSON file per collection in the local workspace.", "A exportação cria uma pasta com um manifesto e um arquivo Extended JSON por coleção no workspace local.", "La exportación crea una carpeta con un manifiesto y un archivo Extended JSON por colección en el workspace local.", "导出会在本地工作区创建一个文件夹，其中包含清单和每个集合一个 Extended JSON 文件。"),
        ("perCollectionLimit", "Limit per collection:", "Limite por coleção:", "Límite por colección:", "每个集合的限制："),
        ("exportSelectedDatabase", "Export selected database", "Exportar banco selecionado", "Exportar base de datos seleccionada", "导出选中的数据库"),
        ("importNote", "Import: choose a folder created by export. Documents are applied by _id upsert; nothing is deleted.", "Importação: informe uma pasta criada pela exportação. Os documentos são aplicados por upsert de _id; nada é apagado.", "Importación: indique una carpeta creada por la exportación. Los documentos se aplican mediante upsert de _id; no se elimina nada.", "导入：请选择由导出创建的文件夹。文档按 _id 执行 upsert；不会删除任何内容。"),
        ("importFolderPlaceholder", "Folder containing manifest.json", "Pasta que contém manifest.json", "Carpeta que contiene manifest.json", "包含 manifest.json 的文件夹"),
        ("chooseFolder", "Choose folder", "Escolher pasta", "Elegir carpeta", "选择文件夹"),
        ("importSelectedDatabase", "Import into selected database", "Importar no banco selecionado", "Importar en la base de datos seleccionada", "导入到选中的数据库"),
        ("serverStatus", "Server status", "Status do servidor", "Estado del servidor", "服务器状态"),
        ("topology", "Topology", "Topologia", "Topología", "拓扑"),
        ("currentOperations", "Current operations", "Operações correntes", "Operaciones actuales", "当前操作"),
        ("currentProfiler", "Current profiler", "Profiler atual", "Profiler actual", "当前分析器"),
        ("users", "Users", "Usuários", "Usuarios", "用户"),
        ("roles", "Roles", "Papéis", "Roles", "角色"),
        ("localAudit", "Local audit", "Auditoria local", "Auditoría local", "本地审计"),
        ("auditJsonFile", "Audit JSON file", "Arquivo JSON da auditoria", "Archivo JSON de auditoría", "审计 JSON 文件"),
        ("exportAudit", "Export audit", "Exportar auditoria", "Exportar auditoría", "导出审计"),
        ("databaseStats", "Database statistics", "Estatísticas do banco", "Estadísticas de la base de datos", "数据库统计"),
        ("collectionStats", "Collection statistics", "Estatísticas da coleção", "Estadísticas de la colección", "集合统计"),
        ("privilegesNote", "Availability depends on MongoDB user privileges.", "A disponibilidade depende dos privilégios do usuário MongoDB.", "La disponibilidad depende de los privilegios del usuario de MongoDB.", "可用性取决于 MongoDB 用户权限。"),
        ("numericOperationId", "Numeric operation ID", "ID numérico da operação", "ID numérico de la operación", "数字操作 ID"),
        ("repeatId", "Repeat the ID to confirm", "Repita o ID para confirmar", "Repita el ID para confirmar", "重复 ID 以确认"),
        ("interruptOperation", "Interrupt operation", "Interromper operação", "Interrumpir operación", "中断操作"),
        ("interruptionIrreversible", "The interruption cannot be undone.", "A interrupção não pode ser desfeita.", "La interrupción no se puede deshacer.", "中断无法撤销。"),
        ("selectedDatabaseName", "Type the selected database name", "Digite o nome do banco selecionado", "Escriba el nombre de la base de datos seleccionada", "输入选中的数据库名称"),
        ("dropDatabase", "Drop database", "Remover banco", "Eliminar base de datos", "删除数据库"),
        ("protectedDatabases", "admin, config and local databases are protected.", "Bancos admin, config e local são protegidos.", "Las bases admin, config y local están protegidas.", "admin、config 和 local 数据库受保护。"),
        ("newDatabase", "New database", "Novo banco", "Nueva base de datos", "新数据库"),
        ("initialCollection", "Initial collection", "Coleção inicial", "Colección inicial", "初始集合"),
        ("repeatDatabase", "Repeat the database name", "Repita o banco", "Repita la base de datos", "重复数据库名称"),
        ("createDatabase", "Create database", "Criar banco", "Crear base de datos", "创建数据库"),
        ("selectedCollectionValidation", "Type the selected collection to validate", "Digite a coleção selecionada para validar", "Escriba la colección seleccionada para validar", "输入要验证的选中集合"),
        ("validateIntegrity", "Validate integrity", "Validar integridade", "Validar integridad", "验证完整性"),
        ("validationMayTake", "Full validation may take time and requires confirmation.", "A validação completa pode ser demorada e exige confirmação.", "La validación completa puede tardar y requiere confirmación.", "完整验证可能需要较长时间并需要确认。"),
        ("selectedCollectionCompact", "Type the selected collection to compact", "Digite a coleção selecionada para compactar", "Escriba la colección seleccionada para compactar", "输入要压缩的选中集合"),
        ("force", "Force", "Forçar", "Forzar", "强制"),
        ("compactCollection", "Compact collection", "Compactar coleção", "Compactar colección", "压缩集合"),
        ("topologyUnavailable", "May be unavailable due to topology or server version.", "Pode estar indisponível pela topologia ou versão do servidor.", "Puede no estar disponible por la topología o la versión del servidor.", "可能因拓扑或服务器版本而不可用。"),
        ("newUser", "New user", "Novo usuário", "Nuevo usuario", "新用户"),
        ("passwordNotPersisted", "Password (not persisted)", "Senha (não persistida)", "Contraseña (no se persiste)", "密码（不持久化）"),
        ("rolesJson", "Roles JSON", "Papéis JSON", "Roles JSON", "角色 JSON"),
        ("repeatUser", "Repeat the user", "Repita o usuário", "Repita el usuario", "重复用户"),
        ("createUser", "Create user", "Criar usuário", "Crear usuario", "创建用户"),
        ("userToRemove", "User to remove", "Usuário a remover", "Usuario que se eliminará", "要删除的用户"),
        ("removeUser", "Remove user", "Remover usuário", "Eliminar usuario", "删除用户"),
        ("removalIrreversible", "Removal is irreversible and requires confirmation.", "A remoção é irreversível e exige confirmação.", "La eliminación es irreversible y requiere confirmación.", "删除不可撤销且需要确认。"),
        ("userForRoles", "User whose roles will change", "Usuário para alterar papéis", "Usuario cuyos roles se cambiarán", "要更改角色的用户"),
        ("revoke", "Revoke", "Revogar", "Revocar", "撤销"),
        ("updateRoles", "Update roles", "Atualizar papéis", "Actualizar roles", "更新角色")
        , ("indexDetail", "Index: {0}", "Índice: {0}", "Índice: {0}", "索引：{0}")
        , ("filterDetail", "Filter: {0}", "Filtro: {0}", "Filtro: {0}", "过滤器：{0}")
        , ("allMatchesAffected", "All matching documents will be affected.", "Todos os documentos correspondentes serão afetados.", "Todos los documentos coincidentes se verán afectados.", "所有匹配的文档都会受到影响。")
        , ("confirmChange", "Confirm change", "Confirmar alteração", "Confirmar cambio", "确认更改")
        , ("openScriptTitle", "Open JavaScript script", "Abrir script JavaScript", "Abrir script JavaScript", "打开 JavaScript 脚本")
        , ("javascriptFileType", "JavaScript", "JavaScript", "JavaScript", "JavaScript")
        , ("allFiles", "All files", "Todos os arquivos", "Todos los archivos", "所有文件")
        , ("saveScriptTitle", "Save JavaScript script", "Salvar script JavaScript", "Guardar script JavaScript", "保存 JavaScript 脚本")
        , ("importMongoFolderTitle", "Select MongoDB import folder", "Selecionar pasta de importação MongoDB", "Seleccionar carpeta de importación de MongoDB", "选择 MongoDB 导入文件夹")
        , ("documentExtendedJsonInsertReplace", "Extended JSON document (insert/replace)", "Documento Extended JSON (inserir/substituir)", "Documento Extended JSON (insertar/reemplazar)", "Extended JSON 文档（插入/替换）")
        , ("insert", "Insert", "Inserir", "Insertar", "插入")
        , ("replaceByFilter", "Replace by filter", "Substituir pelo filtro", "Reemplazar por filtro", "按过滤器替换")
        , ("partialUpdateNote", "Partial update: use operators such as $set, $unset, $inc or $push in the filter tab on the right.", "Atualização parcial: use operadores como $set, $unset, $inc ou $push na aba de filtro à direita.", "Actualización parcial: use operadores como $set, $unset, $inc o $push en la pestaña de filtro de la derecha.", "部分更新：在右侧过滤器选项卡中使用 $set、$unset、$inc 或 $push 等操作符。")
        , ("generateIdentifier", "Generate identifier", "Gerar identificador", "Generar identificador", "生成标识符")
        , ("identifierConstructor", "Identifier in configured constructor", "Identificador no construtor configurado", "Identificador en el constructor configurado", "配置的构造函数中的标识符")
        , ("identifierCanonical", "Identifier in canonical Extended JSON", "Identificador em Extended JSON canônico", "Identificador en Extended JSON canónico", "规范 Extended JSON 中的标识符")
        , ("identifierToInterpret", "Identifier to interpret", "Identificador para interpretar", "Identificador que se interpretará", "要解析的标识符")
        , ("identifierInputPlaceholder", "ObjectId, UUID, 24 hexadecimal digits or UUID with 32 digits", "ObjectId, UUID, 24 dígitos hexadecimais ou UUID com 32 dígitos", "ObjectId, UUID, 24 dígitos hexadecimales o UUID de 32 dígitos", "ObjectId、UUID、24 位十六进制数字或 32 位 UUID")
        , ("interpret", "Interpret", "Interpretar", "Interpretar", "解析")
        , ("interpretedIdentifier", "Type and forms of interpreted identifier", "Tipo e formas do identificador interpretado", "Tipo y formas del identificador interpretado", "解析后标识符的类型和形式")
        , ("requiredMutationFilter", "Required filter to update/delete", "Filtro obrigatório para alterar/excluir", "Filtro obligatorio para actualizar/eliminar", "更新/删除所需的过滤器")
        , ("updateJsonExample", "Update JSON: operators or pipeline, for example [{ \"$set\": { \"active\": true } }]", "Update JSON: operadores ou pipeline, por exemplo [{ \"$set\": { \"ativo\": true } }]", "Update JSON: operadores o pipeline, por ejemplo [{ \"activo\": true }]", "更新 JSON：操作符或管道，例如 [{ \"active\": true }]"),
        ("loadPreview", "Load preview", "Carregar prévia", "Cargar vista previa", "加载预览"),
        ("duplicatePreview", "Duplicate preview", "Duplicar prévia", "Duplicar vista previa", "复制预览"),
        ("updateFields", "Update fields", "Atualizar campos", "Actualizar campos", "更新字段"),
        ("findAndModify", "Find-and-modify", "Find-and-modify", "Find-and-modify", "查找并修改"),
        ("upsert", "Upsert", "Upsert", "Upsert", "Upsert"),
        ("arrayFiltersOptional", "Optional arrayFilters JSON", "arrayFilters JSON opcional", "JSON de arrayFilters opcional", "可选的 arrayFilters JSON"),
        ("deleteByFilter", "Delete by filter", "Excluir pelo filtro", "Eliminar por filtro", "按过滤器删除"),
        ("deleteAllMatches", "Delete all matches", "Excluir todos os correspondentes", "Eliminar todas las coincidencias", "删除所有匹配项"),
        ("firstMatchedPreview", "First matching document (read-only preview)", "Primeiro documento correspondente (prévia somente leitura)", "Primer documento coincidente (vista previa de solo lectura)", "第一个匹配文档（只读预览）"),
        ("afterFindModify", "Document after find-and-modify", "Documento após find-and-modify", "Documento después de find-and-modify", "查找并修改后的文档"),
        ("bulkDocuments", "Extended JSON document array", "Array de documentos Extended JSON", "Matriz de documentos Extended JSON", "Extended JSON 文档数组"),
        ("ordered", "Ordered", "Ordenado", "Ordenado", "有序"),
        ("bulkNote", "The operation is limited to the configured number and respects read-only mode.", "A operação é limitada ao número configurado e respeita o modo somente leitura.", "La operación está limitada al número configurado y respeta el modo de solo lectura.", "操作受配置数量限制，并遵循只读模式。"),
        ("indexKeys", "Index keys", "Chaves do índice", "Claves del índice", "索引键"),
        ("optionalName", "Optional name", "Nome opcional", "Nombre opcional", "可选名称"),
        ("unique", "Unique", "Único", "Único", "唯一"),
        ("sparse", "Sparse", "Esparso", "Disperso", "稀疏"),
        ("hidden", "Hidden", "Oculto", "Oculto", "隐藏"),
        ("ttlSeconds", "TTL (seconds):", "TTL (segundos):", "TTL (segundos):", "TTL（秒）："),
        ("partialFilter", "Optional BSON partial filter, for example: { \"active\": true }", "Filtro parcial BSON opcional, por exemplo: { \"ativo\": true }", "Filtro parcial BSON opcional, por ejemplo: { \"activo\": true }", "可选 BSON 部分过滤器，例如：{ \"active\": true }"),
        ("collationOptional", "Optional BSON collation, for example: { \"locale\": \"pt\", \"strength\": 1 }", "Collation BSON opcional, por exemplo: { \"locale\": \"pt\", \"strength\": 1 }", "Collation BSON opcional, por ejemplo: { \"locale\": \"es\", \"strength\": 1 }", "可选 BSON 排序规则，例如：{ \"locale\": \"zh\", \"strength\": 1 }"),
        ("wildcardProjection", "Optional wildcard projection; requires { \"$**\": 1 }, for example: { \"attributes.color\": 1 }", "Projeção wildcard opcional; requer { \"$**\": 1 }, por exemplo: { \"atributos.cor\": 1 }", "Proyección wildcard opcional; requiere { \"$**\": 1 }, por ejemplo: { \"atributos.color\": 1 }", "可选通配符投影；需要 { \"$**\": 1 }，例如：{ \"attributes.color\": 1 }"),
        ("createIndex", "Create index", "Criar índice", "Crear índice", "创建索引"),
        ("updateList", "Refresh list", "Atualizar lista", "Actualizar lista", "刷新列表"),
        ("indexUsage", "Index usage", "Uso dos índices", "Uso de los índices", "索引使用情况"),
        ("dropIndexName", "Index name to remove", "Nome do índice a remover", "Nombre del índice que se eliminará", "要删除的索引名称"),
        ("indexVisibility", "Index to hide/show", "Índice para ocultar/exibir", "Índice que se ocultará/mostrará", "要隐藏/显示的索引"),
        ("repeatIndex", "Repeat the index", "Repita o índice", "Repita el índice", "重复索引"),
        ("changeVisibility", "Change visibility", "Alterar visibilidade", "Cambiar visibilidad", "更改可见性"),
        ("protectedId", "_id_ is protected", "_id_ é protegido", "_id_ está protegido", "_id_ 受保护"),
        ("indexesOnServer", "Indexes on server", "Índices no servidor", "Índices en el servidor", "服务器上的索引"),
        ("collectionManager", "Manage collections and validation (advanced)", "Gerenciar coleções e validação (avançado)", "Administrar colecciones y validación (avanzado)", "管理集合和验证（高级）"),
        ("newCollectionName", "Name of new collection", "Nome da nova coleção", "Nombre de la nueva colección", "新集合名称"),
        ("maxBytes", "Max. bytes:", "Máx. bytes:", "Máx. bytes:", "最大字节数："),
        ("maxDocs", "Max. docs:", "Máx. docs:", "Máx. docs:", "最大文档数："),
        ("createCollection", "Create collection", "Criar coleção", "Crear colección", "创建集合"),
        ("viewSource", "View source (when View)", "Origem da view (quando View)", "Origen de la vista (cuando es View)", "视图来源（View 模式）"),
        ("viewPipeline", "View pipeline, for example: []", "Pipeline da view, por exemplo: []", "Pipeline de la vista, por ejemplo: []", "视图管道，例如：[]"),
        ("collationBsonOptional", "Optional BSON collation", "Collation BSON opcional", "Collation BSON opcional", "可选 BSON 排序规则"),
        ("clusteredKey", "Clustered BSON key", "Chave clustered BSON", "Clave clustered BSON", "Clustered BSON 键"),
        ("renameCollectionName", "New name for selected collection", "Novo nome da coleção selecionada", "Nuevo nombre de la colección seleccionada", "选中集合的新名称"),
        ("replaceDestination", "Replace destination", "Substituir destino", "Reemplazar destino", "替换目标"),
        ("renameCollection", "Rename collection", "Renomear coleção", "Cambiar nombre de colección", "重命名集合"),
        ("selectedView", "Type the selected view", "Digite a view selecionada", "Escriba la vista seleccionada", "输入选中的视图"),
        ("updateView", "Update view", "Atualizar view", "Actualizar vista", "更新视图"),
        ("loadValidation", "Load validation", "Carregar validação", "Cargar validación", "加载验证"),
        ("validatorJson", "Validator JSON, for example: { \"$jsonSchema\": { \"bsonType\": \"object\" } }", "Validador JSON, por exemplo: { \"$jsonSchema\": { \"bsonType\": \"object\" } }", "Validador JSON, por ejemplo: { \"$jsonSchema\": { \"bsonType\": \"object\" } }", "验证器 JSON，例如：{ \"$jsonSchema\": { \"bsonType\": \"object\" } }"),
        ("selectedCollection", "Type the selected collection", "Digite a coleção selecionada", "Escriba la colección seleccionada", "输入选中的集合"),
        ("applyValidation", "Apply validation", "Aplicar validação", "Aplicar validación", "应用验证"),
        ("removeCollectionLabel", "Remove collection:", "Remover coleção:", "Eliminar colección:", "删除集合："),
        ("removeCollection", "Remove collection", "Remover coleção", "Eliminar colección", "删除集合"),
        ("removeCollectionTooltip", "This action permanently removes the collection after confirmation", "Esta ação remove permanentemente a coleção após a confirmação", "Esta acción elimina permanentemente la colección tras la confirmación", "确认后，此操作将永久删除集合"),
        ("target", "Target…", "Destino…", "Destino…", "目标…"),
        ("executeFullTip", "Run complete content (F5)", "Executar conteúdo completo (F5)", "Ejecutar todo el contenido (F5)", "运行全部内容（F5）"),
        ("cancelRunTip", "Interrupt this tab's execution (Esc)", "Interromper execução desta aba (Esc)", "Interrumpir la ejecución de esta pestaña (Esc)", "中断此标签页的执行（Esc）"),
        ("options", "Options", "Opções", "Opciones", "选项"),
        ("formatCode", "Format JSON/query/script", "Formatar JSON/query/script", "Formatear JSON/consulta/script", "格式化 JSON/查询/脚本"),
        ("formatCodeTip", "Formats the selection or the whole editor; Ctrl+Z undoes. Does not run code.", "Formata a seleção ou o editor inteiro; Ctrl+Z desfaz. Não executa o código.", "Formatea la selección o todo el editor; Ctrl+Z deshace. No ejecuta el código.", "格式化选区或整个编辑器；Ctrl+Z 可撤销。不运行代码。"),
        ("validateSyntax", "Validate syntax", "Validar sintaxe", "Validar sintaxis", "验证语法"),
        ("validateSyntaxTip", "Validates the selection or the whole editor without a connection; selects the error in the text. Does not run the query.", "Valida a seleção ou o editor inteiro sem conexão; seleciona o erro no texto. Não executa a consulta.", "Valida la selección o todo el editor sin conexión; selecciona el error en el texto. No ejecuta la consulta.", "无需连接即可验证选区或整个编辑器；会在文本中选中错误。不执行查询。"),
        ("analyzePipeline", "Analyze pipeline (explain)", "Analisar pipeline (explain)", "Analizar pipeline (explain)", "分析管道（explain）"),
        ("analyzePipelineTip", "Gets queryPlanner for the complete pipeline at this tab's target. Requires an open connection; does not measure execution. Write stages are blocked.", "Obtém queryPlanner do pipeline completo no destino desta aba. Exige conexão aberta; não mede a execução. Stages de escrita são bloqueados.", "Obtiene queryPlanner del pipeline completo en el destino de esta pestaña. Requiere una conexión abierta; no mide la ejecución. Las etapas de escritura están bloqueadas.", "获取此标签页目标上的完整管道 queryPlanner。需要已打开连接；不测量执行。写入阶段会被阻止。"),
        ("inputExtendedJson", "Extended JSON input", "Entrada Extended JSON", "Entrada Extended JSON", "Extended JSON 输入"),
        ("persistInput", "Persist input", "Persistir entrada", "Persistir entrada", "持久化输入"),
        ("projection", "Projection", "Projeção", "Proyección", "投影"),
        ("sorting", "Sort", "Ordenação", "Ordenación", "排序"),
        ("skipDocuments", "Skip documents", "Pular documentos", "Omitir documentos", "跳过文档"),
        ("hintBson", "Hint (BSON)", "Hint (BSON)", "Hint (BSON)", "Hint（BSON）"),
        ("collationBson", "Collation (BSON)", "Collation (BSON)", "Collation (BSON)", "Collation（BSON）"),
        ("batchSize", "Batch size", "Batch size", "Tamaño del lote", "批大小"),
        ("maxTimeMs", "Maximum time (ms)", "Tempo máximo (ms)", "Tiempo máximo (ms)", "最大时间（毫秒）"),
        ("queryComment", "Query comment", "Comentário da consulta", "Comentario de la consulta", "查询注释"),
        ("suggestions", "Suggestions…", "Sugestões…", "Sugerencias…", "建议…"),
        ("firstDocuments", "View first documents", "Ver primeiros documentos", "Ver primeros documentos", "查看首批文档"),
        ("consoleFilterNote", "Write filter, sort, project, skip and limit in Console.", "Escreva filtro, sort, project, skip e limit no Console.", "Escriba filter, sort, project, skip y limit en Console.", "在 Console 中填写 filter、sort、project、skip 和 limit。"),
        ("consoleShortcuts", "Ctrl+Enter: selection or statement at cursor. F5: complete script.", "Ctrl+Enter: seleção ou statement no cursor. F5: script completo.", "Ctrl+Enter: selección o sentencia en el cursor. F5: script completo.", "Ctrl+Enter：光标处的选区或语句。F5：完整脚本。"),
        ("cursorSafetyLimit", "Cursor safety limit (1–1000)", "Limite de segurança por cursor (1–1000)", "Límite de seguridad por cursor (1–1000)", "游标安全限制（1–1000）"),
        ("executionMaxTime", "Maximum execution time (ms)", "Tempo máximo da execução (ms)", "Tiempo máximo de ejecución (ms)", "最大执行时间（毫秒）"),
        ("history", "History…", "Histórico…", "Historial…", "历史记录…"),
        ("scriptEditor", "Script or query editor", "Editor de script ou consulta", "Editor de script o consulta", "脚本或查询编辑器"),
        ("suggestion", "Suggestion", "Sugestão", "Sugerencia", "建议"),
        ("contextualSuggestions", "Contextual suggestions", "Sugestões contextuais", "Sugerencias contextuales", "上下文建议"),
        ("selectedSuggestionDetails", "Details of selected suggestion", "Detalhes da sugestão selecionada", "Detalles de la sugerencia seleccionada", "所选建议的详细信息"),
        ("aiSuggestion", "Local AI suggestion", "Sugestão da IA local", "Sugerencia de IA local", "本地 AI 建议"),
        ("generating", "Generating", "Gerando", "Generando", "正在生成"),
        ("aiPreview", "Local AI suggestion preview", "Prévia da sugestão da IA local", "Vista previa de la sugerencia de IA local", "本地 AI 建议预览"),
        ("resultVisualization", "Result view", "Visualização dos resultados", "Vista de resultados", "结果视图"),
        ("formattedJsonResults", "Formatted JSON results", "Resultados em JSON formatado", "Resultados en JSON formateado", "格式化 JSON 结果"),
        ("treeResults", "Tree results", "Resultados em árvore", "Resultados en árbol", "树形结果"),
        ("copyJsonDocument", "Copy JSON", "Copiar JSON", "Copiar JSON", "复制 JSON"),
        ("previous", "Previous", "Anterior", "Anterior", "上一页"),
        ("next", "Next", "Próxima", "Siguiente", "下一页"),
        ("exportPage", "Export page…", "Exportar página…", "Exportar página…", "导出页面…"),
        ("exportPageTip", "Export only the loaded page in Extended JSON or CSV (formula protection)", "Exportar somente a página carregada em Extended JSON ou CSV (proteção de fórmulas)", "Exportar solo la página cargada en Extended JSON o CSV (protección contra fórmulas)", "仅以 Extended JSON 或 CSV 导出已加载页面（防止公式）"),
        ("results", "Results", "Resultados", "Resultados", "结果"),
        ("messages", "Messages", "Mensagens", "Mensajes", "消息"),
        ("errors", "Errors", "Erros", "Errores", "错误"),
        ("executionMessages", "Execution messages", "Mensagens de execução", "Mensajes de ejecución", "执行消息"),
        ("executionErrors", "Execution errors", "Erros de execução", "Errores de ejecución", "执行错误"),
        ("structuredDocuments", "Documents", "Documentos", "Documentos", "文档"),
        ("structuredDocumentsAccessible", "Structured documents", "Documentos estruturados", "Documentos estructurados", "结构化文档"),
        ("executionResult", "Execution result", "Resultado da execução", "Resultado de la ejecución", "执行结果"),
        ("copy", "Copy", "Copiar", "Copiar", "复制"),
        ("openInEditor", "Open in editor", "Abrir no editor", "Abrir en el editor", "在编辑器中打开"),
        ("insertEllipsis", "Insert…", "Inserir…", "Insertar…", "插入…"),
        ("editEllipsis", "Edit…", "Editar…", "Editar…", "编辑…"),
        ("deleteEllipsis", "Delete…", "Excluir…", "Eliminar…", "删除…"),
        ("refreshPage", "Refresh page", "Atualizar página", "Actualizar página", "刷新页面"),
        ("runScript", "Run script", "Executar script", "Ejecutar script", "运行脚本"),
        ("runScriptTip", "Runs the complete script again; writes require confirmation.", "Executa novamente o script completo; escritas exigem confirmação.", "Ejecuta de nuevo el script completo; las escrituras requieren confirmación.", "再次运行完整脚本；写入需要确认。"),
        ("documentsOnPage", "Documents on page", "Documentos da página", "Documentos de la página", "页面文档"),
        ("documentFields", "Document fields", "Campos do documento", "Campos del documento", "文档字段"),
        ("send", "Send", "Enviar", "Enviar", "发送")
        , ("connect", "Connect", "Conectar", "Conectar", "连接")
        , ("instanceInfo", "Instance information", "Informações da instância", "Información de la instancia", "实例信息")
        , ("disconnect", "Disconnect", "Desconectar", "Desconectar", "断开连接")
        , ("refresh", "Refresh", "Atualizar", "Actualizar", "刷新")
        , ("openDocumentsQuery", "Open documents / query", "Abrir documentos / consulta", "Abrir documentos / consulta", "打开文档 / 查询")
        , ("viewIndexes", "View indexes", "Ver índices", "Ver índices", "查看索引")
        , ("detailsStats", "Details and statistics", "Detalhes e estatísticas", "Detalles y estadísticas", "详细信息和统计")
        , ("openShellScript", "Open shell / script", "Abrir shell / script", "Abrir shell / script", "打开 shell / 脚本")
        , ("createCollectionScript", "Collection creation script", "Script de criação de coleção", "Script de creación de colección", "集合创建脚本")
        , ("dropDatabaseScript", "Database removal script", "Script de remoção do banco", "Script de eliminación de la base de datos", "数据库删除脚本")
        , ("generateCrudScript", "Generate CRUD script", "Gerar script CRUD", "Generar script CRUD", "生成 CRUD 脚本")
        , ("find", "Find", "Find", "Find", "Find")
        , ("insertScript", "Insert", "Insert", "Insert", "Insert")
        , ("updateScript", "Update", "Update", "Update", "Update")
        , ("deleteScript", "Delete", "Delete", "Delete", "Delete")
        , ("createIndexScript", "Create Index", "Script de criação do índice", "Script de creación del índice", "创建索引")
        , ("dropCollectionScript", "Drop Collection", "Remover coleção", "Eliminar colección", "删除集合")
        , ("statisticsScript", "Statistics", "Estatísticas", "Estadísticas", "统计")
        , ("removeIndexEllipsis", "Remove index…", "Remover índice…", "Eliminar índice…", "删除索引…")
        , ("copyDefinition", "Copy definition", "Copiar definição", "Copiar definición", "复制定义")
        , ("noConnectionsHeading", "Your databases appear here", "Seus bancos aparecem aqui", "Sus bases de datos aparecen aquí", "您的数据库会显示在这里")
        , ("noConnectionsNote", "Open a connection to load databases. Collections load when expanded.", "Abra uma conexão para carregar os bancos. As coleções serão carregadas ao expandir.", "Abra una conexión para cargar las bases de datos. Las colecciones se cargan al expandir.", "打开连接以加载数据库。展开时会加载集合。")
        , ("itemDetails", "Item details", "Detalhes do item", "Detalles del elemento", "项目详细信息")
        , ("loading", "Loading…", "Carregando…", "Cargando…", "正在加载…")
        , ("instanceTopology", "Instances / topology…", "Instâncias / topologia…", "Instancias / topología…", "实例 / 拓扑…")
        , ("explorerDetails", "Explorer details", "Detalhes do explorer", "Detalles del explorador", "浏览器详细信息")
        , ("closeTab", "Close tab", "Fechar aba", "Cerrar pestaña", "关闭标签页")
        , ("workspace", "Your workspace", "Seu espaço de trabalho", "Su espacio de trabajo", "您的工作区")
        , ("workspaceNote", "Open a collection or create a script to begin.", "Abra uma coleção ou crie um script para começar.", "Abra una colección o cree un script para comenzar.", "打开集合或创建脚本以开始。")
        , ("operationProgress", "Operation progress", "Progresso da operação", "Progreso de la operación", "操作进度")
        , ("cancelHighlightedOperation", "Cancel highlighted operation", "Cancelar operação em destaque", "Cancelar operación destacada", "取消突出显示的操作")
        , ("autocompleteTitle", "Autocomplete and local AI", "Autocomplete e IA local", "Autocompletado e IA local", "自动补全和本地 AI")
        , ("localProcessingNote", "Local AI runs on this device. Autocomplete uses only the context options below; the AI Agent asks for the context scope of each message explicitly and shows it before sending.", "A IA local roda neste dispositivo. O autocomplete usa só as opções de contexto abaixo; o Agente IA pede explicitamente o escopo de contexto de cada mensagem e o mostra antes do envio.", "La IA local se ejecuta en este dispositivo. El autocompletado usa solo las opciones de contexto de abajo; el Agente IA pide explícitamente el alcance del contexto de cada mensaje y lo muestra antes del envío.", "本地 AI 在此设备上运行。自动补全仅使用下方的上下文选项；AI 智能体会为每条消息明确询问上下文范围，并在发送前显示。")
        , ("enableAutocomplete", "Enable autocomplete", "Habilitar autocomplete", "Habilitar autocompletado", "启用自动补全")
        , ("useLocalDictionary", "Use local dictionary", "Usar dicionário local", "Usar diccionario local", "使用本地字典")
        , ("useInputContext", "Use Input context", "Usar contexto do Input", "Usar contexto de Input", "使用 Input 上下文")
        , ("useResultFields", "Use fields from Results", "Usar campos dos Resultados", "Usar campos de Resultados", "使用结果中的字段")
        , ("useEditorHistoryContext", "Use expanded editor and history context", "Usar contexto ampliado do editor e histórico", "Usar contexto ampliado del editor y el historial", "使用扩展的编辑器和历史记录上下文")
        , ("acceptPartialTab", "Accept suggestion in parts with Tab", "Aceitar sugestão em partes com Tab", "Aceptar sugerencias por partes con Tab", "使用 Tab 分段接受建议")
        , ("useLocalModelAssistant", "Use local model in the AI Agent", "Usar o modelo local no Agente IA", "Usar el modelo local en el Agente IA", "在 AI 智能体中使用本地模型")
        , ("inlineSuggestion", "Automatic suggestion (inline)", "Sugestão automática (inline)", "Sugerencia automática (inline)", "自动建议（内联）")
        , ("suggestAfterPause", "Suggest automatically after a typing pause", "Sugerir automaticamente após uma pausa na digitação", "Sugerir automáticamente tras una pausa al escribir", "输入暂停后自动建议")
        , ("useDefault", "Use default", "Usar padrão", "Usar predeterminado", "使用默认值")
        , ("resetInlineEnabledTip", "Follows the product default (enabled) instead of the value saved in this session.", "Volta a acompanhar o padrão do produto (ligado) em vez do valor salvo nesta sessão.", "Vuelve a seguir el valor predeterminado del producto (activado) en lugar del valor guardado en esta sesión.", "恢复使用产品默认值（启用），而不是本会话保存的值。")
        , ("useDeterministicSuggestions", "Use deterministic suggestions (catalog and schema)", "Usar sugestões determinísticas (catálogo e schema)", "Usar sugerencias deterministas (catálogo y esquema)", "使用确定性建议（目录和模式）")
        , ("deterministicSuggestionsAccessible", "Use deterministic suggestions for automatic suggestion", "Usar sugestões determinísticas na sugestão automática", "Usar sugerencias deterministas en la sugerencia automática", "在自动建议中使用确定性建议")
        , ("resetDictionaryTip", "Follows Use local dictionary instead of the value saved in this session.", "Volta a seguir Usar dicionário local em vez do valor salvo nesta sessão.", "Vuelve a seguir Usar diccionario local en lugar del valor guardado en esta sesión.", "恢复使用本地字典，而不是本会话保存的值。")
        , ("useAiSuggestions", "Use local AI in automatic suggestions", "Usar IA local nas sugestões automáticas", "Usar IA local en las sugerencias automáticas", "在自动建议中使用本地 AI")
        , ("aiSuggestionsAccessible", "Use local AI in automatic suggestion", "Usar IA local nas sugestões automáticas", "Usar IA local en la sugerencia automática", "在自动建议中使用本地 AI")
        , ("resetAiTip", "Follows the product default (disabled) instead of the value saved in this session.", "Volta a acompanhar o padrão do produto (desligado) em vez do valor salvo nesta sessão.", "Vuelve a seguir el valor predeterminado del producto (desactivado) en lugar del valor guardado en esta sesión.", "恢复使用产品默认值（禁用），而不是本会话保存的值。")
        , ("inlineDisabledNote", "The two options above have no effect while automatic suggestion is disabled.", "As duas opções acima não têm efeito enquanto a sugestão automática estiver desligada.", "Las dos opciones anteriores no tienen efecto mientras la sugerencia automática esté desactivada.", "自动建议禁用时，上述两个选项不起作用。")
        , ("inlineAiLoadNote", "Local AI only enters automatic suggestion when a model is already loaded; typing never loads the model by itself.", "A IA local só entra na sugestão automática quando um modelo já estiver carregado; digitar nunca carrega o modelo sozinho.", "La IA local solo entra en la sugerencia automática cuando ya hay un modelo cargado; escribir nunca carga el modelo por sí solo.", "只有在模型已加载时本地 AI 才会参与自动建议；输入不会自行加载模型。")
        , ("suggestionList", "Suggestion list (Ctrl+Space)", "Lista de sugestões (Ctrl+Espaço)", "Lista de sugerencias (Ctrl+Espacio)", "建议列表（Ctrl+Space）")
        , ("openOnTrigger", "Open the list when typing . or $", "Abrir a lista ao digitar . ou $", "Abrir la lista al escribir . o $", "输入 . 或 $ 时打开列表")
        , ("openOnTriggerAccessible", "Open the list when typing dot or dollar", "Abrir a lista ao digitar ponto ou cifrão", "Abrir la lista al escribir punto o dólar", "输入点号或美元符号时打开列表")
        , ("enterAccepts", "Enter also accepts the list suggestion", "Enter também confirma a sugestão da lista", "Enter también acepta la sugerencia de la lista", "Enter 也接受列表建议")
        , ("autocompleteMode", "Autocomplete mode", "Modo de autocomplete", "Modo de autocompletado", "自动补全模式")
        , ("mode", "Mode", "Modo", "Modo", "模式")
        , ("modelsDirectory", "Model directory", "Diretório de modelos", "Directorio de modelos", "模型目录")
        , ("browse", "Browse…", "Procurar…", "Examinar…", "浏览…")
        , ("openFolder", "Open folder", "Abrir pasta", "Abrir carpeta", "打开文件夹")
        , ("openModelsFolderTip", "Opens the model directory in the file manager. Downloads are saved there.", "Abre o diretório de modelos no gerenciador de arquivos. Os downloads são salvos nele.", "Abre el directorio de modelos en el administrador de archivos. Las descargas se guardan allí.", "在文件管理器中打开模型目录。下载内容会保存到那里。")
        , ("openModelsDirectoryAccessible", "Open model directory", "Abrir diretório de modelos", "Abrir directorio de modelos", "打开模型目录")
        , ("modelsDirectoryNote", "Each subfolder is a model. Empty uses the default directory shown in the field.", "Cada subpasta é um modelo. Vazio usa o diretório padrão indicado no campo.", "Cada subcarpeta es un modelo. Vacío usa el directorio predeterminado indicado en el campo.", "每个子文件夹都是一个模型。留空将使用字段中显示的默认目录。")
        , ("model", "Model", "Modelo", "Modelo", "模型")
        , ("noModelSelected", "No model selected", "Nenhum modelo selecionado", "Ningún modelo seleccionado", "未选择模型")
        , ("update", "Update", "Atualizar", "Actualizar", "更新")
        , ("otherFolder", "Another folder…", "Outra pasta…", "Otra carpeta…", "其他文件夹…")
        , ("externalModelFolder", "Use external model folder", "Usar pasta de modelo externa", "Usar carpeta de modelo externa", "使用外部模型文件夹")
        , ("downloadModel", "Download model", "Baixar modelo", "Descargar modelo", "下载模型")
        , ("download", "Download", "Baixar", "Descargar", "下载")
        , ("downloadTip", "Downloads the selected model to the model directory", "Baixa o modelo escolhido para o diretório de modelos", "Descarga el modelo elegido al directorio de modelos", "将所选模型下载到模型目录")
        , ("cancelDownload", "Cancel model download", "Cancelar download do modelo", "Cancelar descarga del modelo", "取消模型下载")
        , ("refreshDownloadModels", "Refresh model download list", "Atualizar lista de modelos para baixar", "Actualizar la lista de modelos para descargar", "刷新可下载模型列表")
        , ("downloadProgress", "Model download progress", "Progresso do download do modelo", "Progreso de descarga del modelo", "模型下载进度")
        , ("modelDetailsHuggingFace", "View model details on Hugging Face", "Ver detalhes do modelo no Hugging Face", "Ver detalles del modelo en Hugging Face", "在 Hugging Face 查看模型详情")
        , ("modelDownloadStatus", "Model download status", "Estado do download do modelo", "Estado de descarga del modelo", "模型下载状态")
        , ("hardware", "Hardware", "Hardware", "Hardware", "硬件")
        , ("localAiHardware", "Local AI hardware", "Hardware da IA local", "Hardware de la IA local", "本地 AI 硬件")
        , ("detected", "Detected", "Detectado", "Detectado", "已检测")
        , ("hardwareDetectionNote", "Automatic tries NPU, GPU and CPU, in that order, among those available and compatible with the model, and records the provider used. Explicitly selected CPU, GPU or NPU does not fall back to other hardware: a failure is reported. Without a valid model, basic suggestions remain available.", "Automático tenta NPU, GPU e CPU, nessa ordem, entre os disponíveis e compatíveis com o modelo, e registra o provider usado. CPU, GPU ou NPU escolhidos explicitamente não recorrem a outro hardware: uma falha é informada. Sem modelo válido, as sugestões básicas continuam disponíveis.", "Automático prueba NPU, GPU y CPU, en ese orden, entre los disponibles y compatibles con el modelo, y registra el proveedor usado. CPU, GPU o NPU elegidos explícitamente no recurren a otro hardware: se informa el fallo. Sin un modelo válido, las sugerencias básicas siguen disponibles.", "自动模式会按 NPU、GPU、CPU 的顺序，在可用且兼容模型的硬件中尝试，并记录所使用的 provider。显式选择 CPU、GPU 或 NPU 时不会回退到其他硬件：会报告失败。没有有效模型时，基本建议仍可用。")
        , ("estimationProfile", "Estimation profile", "Perfil de estimativa", "Perfil de estimación", "估算配置")
        , ("useDetectedHardware", "Use detected hardware", "Usar hardware detectado", "Usar hardware detectado", "使用检测到的硬件")
        , ("hardwareProfile", "Manual hardware profile", "Perfil manual de hardware", "Perfil manual de hardware", "手动硬件配置")
        , ("vendor", "Vendor", "Fabricante", "Fabricante", "厂商")
        , ("gpuProfile", "GPU / profile", "GPU / perfil", "GPU / perfil", "GPU / 配置")
        , ("gpuMemory", "GPU memory in GiB", "Memória da GPU em GiB", "Memoria de GPU en GiB", "GPU 内存（GiB）")
        , ("add", "Add", "Adicionar", "Añadir", "添加")
        , ("addHardwareProfile", "Add hardware profile", "Adicionar perfil de hardware", "Añadir perfil de hardware", "添加硬件配置")
        , ("removeSelectedProfile", "Remove selected profile", "Remover perfil selecionado", "Eliminar perfil seleccionado", "删除选中的配置")
        , ("hardwareMemoryNote", "Usable memory reserves room for the system, driver and runtime. Manual profiles are for estimation and do not change the real provider.", "A memória utilizável reserva margem para sistema, driver e runtime. Perfis manuais servem para estimativa e não alteram o provider real.", "La memoria utilizable reserva margen para el sistema, el controlador y el runtime. Los perfiles manuales sirven para estimación y no cambian el proveedor real.", "可用内存会为系统、驱动和运行时预留空间。手动配置仅用于估算，不会更改实际 provider。")
        , ("contextTokens", "Context tokens", "Tokens de contexto", "Tokens de contexto", "上下文令牌")
        , ("maximumGeneratedTokens", "Maximum generated tokens", "Máximo de tokens gerados", "Máximo de tokens generados", "最大生成令牌数")
        , ("typeOrSelect", "Type or select", "Digite ou selecione", "Escriba o seleccione", "输入或选择")
        , ("inlineDelay", "Pause before automatic suggestion (ms)", "Pausa até a sugestão automática (ms)", "Pausa hasta la sugerencia automática (ms)", "自动建议前的暂停时间（毫秒）")
        , ("inlineDelayTip", "Time without typing before the automatic suggestion (inline) appears at the cursor.", "Tempo sem digitar até a sugestão automática (inline) aparecer no cursor.", "Tiempo sin escribir hasta que la sugerencia automática (inline) aparezca en el cursor.", "停止输入后，自动建议（内联）在光标处出现前的等待时间。")
        , ("tokenBudgetValidation", "Token budget validation", "Validação do orçamento de tokens", "Validación del presupuesto de tokens", "令牌预算验证")
        , ("inlineHint", "Suggestion appears at the cursor. Tab advances; Esc dismisses. Ctrl+Space keeps Console/MQL suggestions. Download a model above or copy an ONNX GenAI export to the model directory.", "A sugestão aparece no cursor. Tab avança; Esc descarta. Ctrl+Espaço mantém as sugestões de Console/MQL. Baixe um modelo acima ou copie uma exportação ONNX GenAI para o diretório de modelos.", "La sugerencia aparece en el cursor. Tab avanza; Esc descarta. Ctrl+Espacio mantiene las sugerencias de Console/MQL. Descargue un modelo arriba o copie una exportación ONNX GenAI al directorio de modelos.", "建议会显示在光标处。Tab 前进；Esc 忽略。Ctrl+Space 保留 Console/MQL 建议。请在上方下载模型，或将 ONNX GenAI 导出复制到模型目录。")
        , ("testResult", "Test result", "Resultado do teste", "Resultado de la prueba", "测试结果")
        , ("modelTestResult", "Model test result", "Resultado do teste do modelo", "Resultado de la prueba del modelo", "模型测试结果")
        , ("closeWindow", "Close", "Fechar", "Cerrar", "关闭")
        , ("testModel", "Test model", "Testar modelo", "Probar modelo", "测试模型")
        , ("modelDirectoryPicker", "Model directory", "Diretório de modelos", "Directorio de modelos", "模型目录")
        , ("onnxModelFolderPicker", "ONNX GenAI model folder", "Pasta de um modelo ONNX GenAI", "Carpeta de un modelo ONNX GenAI", "ONNX GenAI 模型文件夹")
        , ("uuidRepresentationsPreview", "Preview of UUID representations", "Prévia das representações UUID", "Vista previa de las representaciones UUID", "UUID 表示形式预览")
        , ("statusReady", "Ready", "Pronto", "Listo", "就绪")
        , ("statusRunning", "Running", "Em andamento", "En curso", "进行中")
        , ("statusSuccess", "Completed", "Concluído", "Completado", "已完成")
        , ("statusError", "Error", "Erro", "Error", "错误")
        , ("statusCancelled", "Canceled", "Cancelado", "Cancelado", "已取消")
        , ("statusWarning", "Warning", "Aviso", "Aviso", "警告")
        , ("additionalOperations", "+{0} operations", "+{0} operações", "+{0} operaciones", "+{0} 个操作")
        , ("notExecuted", "Press Run to view results in Extended JSON.", "Execute para visualizar os resultados em Extended JSON.", "Ejecute para ver los resultados en Extended JSON.", "运行后可查看 Extended JSON 结果。")
        , ("untitledScript", "Untitled.js", "Sem título.js", "Sin título.js", "未命名.js")
        , ("noConnection", "No connection", "Sem conexão", "Sin conexión", "无连接")
        , ("chooseDatabase", "Choose a database", "Escolha um banco", "Elija una base de datos", "选择数据库")
        , ("chooseConnectionForTab", "Choose a connection for this tab.", "Escolha uma conexão para esta aba.", "Elija una conexión para esta pestaña.", "为此标签页选择连接。")
        , ("disconnectedOpenConnection", "Disconnected — open the connection to run.", "Desconectado — abra a conexão para executar.", "Desconectado — abra la conexión para ejecutar.", "已断开连接 — 打开连接后才能运行。")
        , ("readOnlyPrefix", "Read-only · ", "Somente leitura · ", "Solo lectura · ", "只读 · ")
        , ("fixedTargetPrefix", "Fixed target for this tab · ", "Destino fixo desta aba · ", "Destino fijo de esta pestaña · ", "此标签页的固定目标 · ")
        , ("executing", "Running…", "Executando…", "Ejecutando…", "正在运行…")
        , ("executingQuery", "Running query in {0} › {1}", "Executando consulta em {0} › {1}", "Ejecutando consulta en {0} › {1}", "正在 {0} › {1} 中运行查询")
        , ("consoleNoResult", "No expression returned a result. See Messages.", "Nenhuma expressão retornou resultado. Consulte Mensagens.", "Ninguna expresión devolvió resultados. Consulte Mensajes.", "没有表达式返回结果。请查看消息。")
        , ("scriptNoDocuments", "The script produced no documents. See Messages.", "O script não emitiu documentos. Consulte Mensagens.", "El script no produjo documentos. Consulte Mensajes.", "脚本未生成文档。请查看消息。")
        , ("noDocumentsFound", "No documents found.", "Nenhum documento encontrado.", "No se encontraron documentos.", "未找到文档。")
        , ("timedOut", "Timed out", "Tempo limite excedido", "Tiempo agotado", "已超时")
        , ("scriptFailed", "Failed — code {0}", "Falha — código {0}", "Falló — código {0}", "失败 — 代码 {0}")
        , ("resultLimited", " · result limited", " · resultado limitado", " · resultado limitado", " · 结果已限制")
        , ("documentsMetrics", "{0} document(s) · limit {1} · {2} ms", "{0} documento(s) · limite {1} · {2} ms", "{0} documento(s) · límite {1} · {2} ms", "{0} 个文档 · 限制 {1} · {2} 毫秒")
        , ("consoleMetrics", "{0} result(s) · {1} ms · {2} · maximum {3} documents per cursor", "{0} resultado(s) · {1} ms · {2} · máximo {3} documentos por cursor", "{0} resultado(s) · {1} ms · {2} · máximo {3} documentos por cursor", "{0} 个结果 · {1} 毫秒 · {2} · 每个游标最多 {3} 个文档")
        , ("scriptMetrics", "{0} document(s) · {1} ms", "{0} documento(s) · {1} ms", "{0} documento(s) · {1} ms", "{0} 个文档 · {1} 毫秒")
        , ("queryInterrupted", "Execution interrupted.", "Execução interrompida.", "Ejecución interrumpida.", "执行已中断。")
        , ("writeEffectsNotReverted", "Effects already sent to MongoDB are not reverted automatically. The server result may be uncertain.", "Efeitos já enviados ao MongoDB não são revertidos automaticamente. O resultado no servidor pode ser incerto.", "Los efectos ya enviados a MongoDB no se revierten automáticamente. El resultado en el servidor puede ser incierto.", "已发送到 MongoDB 的效果不会自动回滚。服务器上的结果可能不确定。")
        , ("executionFailed", "Execution failed", "Falha na execução", "Falló la ejecución", "执行失败")
        , ("executionIncomplete", "The execution could not be completed.", "Não foi possível concluir a execução.", "No se pudo completar la ejecución.", "无法完成执行。")
        , ("historyNotSaved", "History was not saved: {0}", "Histórico não salvo: {0}", "No se guardó el historial: {0}", "历史记录未保存：{0}")
        , ("queryCompletedHistoryNotSaved", "Query completed; history was not saved: {0}", "Consulta concluída; histórico não salvo: {0}", "Consulta completada; no se guardó el historial: {0}", "查询已完成；历史记录未保存：{0}")
        , ("savedFile", "File saved", "Arquivo salvo", "Archivo guardado", "文件已保存")
        , ("savedFileHistoryNotSaved", "File saved; history was not saved: {0}", "Arquivo salvo; histórico não salvo: {0}", "Archivo guardado; no se guardó el historial: {0}", "文件已保存；历史记录未保存：{0}")
        , ("analyzingPipeline", "Analyzing pipeline…", "Analisando pipeline…", "Analizando pipeline…", "正在分析管道…")
        , ("capturedPipelinePlan", "Pipeline plan captured — {0} › {1} › {2}\nqueryPlanner: estimated plan; it does not measure duration or the actual document count.\n\n{3}", "Plano do pipeline capturado — {0} › {1} › {2}\nqueryPlanner: plano estimado; não mede a duração nem a quantidade real de documentos.\n\n{3}", "Plan del pipeline capturado — {0} › {1} › {2}\nqueryPlanner: plan estimado; no mide la duración ni la cantidad real de documentos.\n\n{3}", "已捕获管道计划 — {0} › {1} › {2}\nqueryPlanner：估算计划；不测量持续时间或实际文档数量。\n\n{3}")
        , ("planAvailable", "Plan available", "Plano disponível", "Plan disponible", "计划可用")
        , ("analysisCancelled", "Analysis canceled. Effects already sent to the server are not reverted.", "Análise cancelada. Efeitos já enviados ao servidor não são revertidos.", "Análisis cancelado. Los efectos ya enviados al servidor no se revierten.", "分析已取消。已发送到服务器的效果不会回滚。")
        , ("analysisFailed", "Analysis failed", "Falha na análise", "Falló el análisis", "分析失败")
        , ("copySelectedJsonHint", "Copy formatted JSON from {0}", "Copiar o JSON formatado de {0}", "Copiar el JSON formateado de {0}", "复制 {0} 的格式化 JSON")
        , ("selectDocumentToCopy", "Select a document in the results to copy.", "Selecione um documento nos resultados para copiar.", "Seleccione un documento en los resultados para copiar.", "选择结果中的文档以复制。")
        , ("invalidJsonNoEdit", "Invalid JSON: there is no document to edit.", "JSON inválido: não há documento para editar.", "JSON no válido: no hay documento para editar.", "JSON 无效：没有可编辑的文档。")
        , ("resultNoCollection", "Result has no known source collection. Use Console to query the collection before editing.", "Resultado sem coleção de origem conhecida. Consulte a coleção no Console para editar.", "El resultado no tiene una colección de origen conocida. Consulte la colección en Console para editar.", "结果没有已知的源集合。请在 Console 中查询集合后再编辑。")
        , ("resultNoIdentity", "Document has no _id: there is no safe identity to write. Run the query without excluding _id.", "Documento sem _id: não há identidade segura para gravar. Refaça a consulta sem excluir _id.", "El documento no tiene _id: no hay una identidad segura para escribir. Ejecute la consulta sin excluir _id.", "文档没有 _id：没有安全的写入标识。请不要排除 _id 后重新查询。")
        , ("partialProjectionEdit", "Partial projection: the copy does not contain the complete document. Run the query without projection to edit.", "Projeção parcial: a cópia não contém o documento completo. Execute a consulta sem projeção para editar.", "Proyección parcial: la copia no contiene el documento completo. Ejecute la consulta sin proyección para editar.", "部分投影：副本不包含完整文档。请取消投影后重新查询再编辑。")
        , ("derivedResultEdit", "Aggregation result: it may not correspond to a stored document. Use find to edit.", "Resultado de agregação: pode não corresponder a um documento armazenado. Use find para editar.", "Resultado de agregación: puede no corresponder a un documento almacenado. Use find para editar.", "聚合结果可能不对应存储的文档。请使用 find 编辑。")
        , ("unknownOriginEdit", "Origin has no safe identity. Use find on the collection to edit.", "Origem sem identidade segura. Use find na coleção para editar.", "El origen no tiene una identidad segura. Use find en la colección para editar.", "来源没有安全标识。请在集合上使用 find 编辑。")
        , ("waitExecutionBeforeSave", "Wait for this tab's execution to finish.", "Aguarde a execução desta aba terminar.", "Espere a que termine la ejecución de esta pestaña.", "请等待此标签页的执行完成。")
        , ("connectionClosedBeforeSave", "This tab's connection is closed: open the connection before saving.", "Conexão desta aba fechada: abra a conexão antes de salvar.", "La conexión de esta pestaña está cerrada: abra la conexión antes de guardar.", "此标签页的连接已关闭：打开连接后再保存。")
        , ("noDocumentInResult", "No document in this result.", "Nenhum documento neste resultado.", "No hay ningún documento en este resultado.", "此结果中没有文档。")
        , ("updatingBsonPresentation", "Updating BSON presentation", "Atualizando apresentação BSON", "Actualizando la presentación BSON", "正在更新 BSON 表示")
        , ("bsonPresentationUpdated", "BSON presentation updated", "Apresentação BSON atualizada", "Presentación BSON actualizada", "BSON 表示已更新")
        , ("bsonPresentationCancelled", "BSON presentation update canceled", "Atualização da apresentação cancelada", "Actualización de la presentación cancelada", "BSON 表示更新已取消")
        , ("bsonPresentationFailed", "BSON presentation failed", "Falha na apresentação BSON", "Falló la presentación BSON", "BSON 表示失败")
        , ("noResults", "No results.", "Nenhum resultado.", "Sin resultados.", "没有结果。")
        , ("documentCountSuffix", "{0} document(s)", "{0} documento(s)", "{0} documento(s)", "{0} 个文档")
        , ("truncatedSuffix", " · limited", " · limitado", " · limitado", " · 已限制")
        , ("partialProjectionSuffix", " · partial projection", " · projeção parcial", " · proyección parcial", " · 部分投影")
        , ("legacyUuidSuffix", " · {0} legacy UUID(s) of unknown origin", " · {0} UUID(s) legado(s) de origem desconhecida", " · {0} UUID(s) heredado(s) de origen desconocido", " · {0} 个来源未知的旧版 UUID")
        , ("readyPeriod", "Ready.", "Pronto.", "Listo.", "就绪。")
        , ("localWorkspaceFooter", "Local workspace: LiteDB. Persisted credentials will later use the system vault.", "Workspace local: LiteDB. Credenciais persistidas serão adicionadas com cofre do sistema.", "Workspace local: LiteDB. Las credenciales persistidas usarán posteriormente el almacén del sistema.", "本地工作区：LiteDB。持久化凭据将通过系统保险库添加。")
        , ("selectConnectionToStart", "Select a connection to begin.", "Selecione uma conexão para começar.", "Seleccione una conexión para comenzar.", "选择连接以开始。")
        , ("clickLoadDatabases", "1. Click Load databases.", "1. Clique em Carregar bancos.", "1. Haga clic en Cargar bases de datos.", "1. 点击加载数据库。")
        , ("chooseCollectionQuery", "2. Choose a collection to enable the query.", "2. Escolha uma coleção para habilitar a consulta.", "2. Elija una colección para habilitar la consulta.", "2. 选择集合以启用查询。")
        , ("readyContext", "Ready: {0}.{1}. Adjust the filter and run.", "Pronto: {0}.{1}. Ajuste o filtro e execute.", "Listo: {0}.{1}. Ajuste el filtro y ejecute.", "就绪：{0}.{1}。调整过滤器并运行。")
        , ("noConnectionSelected", "No connection selected.", "Nenhuma conexão selecionada.", "Ninguna conexión seleccionada.", "未选择连接。")
        , ("folderValue", "Folder: {0}", "Pasta: {0}", "Carpeta: {0}", "文件夹：{0}")
        , ("noFolder", "no folder", "sem pasta", "sin carpeta", "无文件夹")
        , ("defaultDatabaseValue", "Default database: {0}", "Banco padrão: {0}", "Base de datos predeterminada: {0}", "默认数据库：{0}")
        , ("notDefined", "not defined", "não definido", "no definida", "未定义")
        , ("environmentValue", "{0}", "{0}", "{0}", "{0}")
        , ("noEnvironment", "no environment", "sem ambiente", "sin entorno", "无环境")
        , ("lastConnectionValue", "Last connection: {0}", "Última conexão: {0}", "Última conexión: {0}", "上次连接：{0}")
        , ("never", "never", "nunca", "nunca", "从未")
        , ("autocompleteFieldsNote", "Load a sample or run a query to suggest fields from this collection.", "Carregue uma amostra ou execute uma consulta para sugerir campos desta coleção.", "Cargue una muestra o ejecute una consulta para sugerir campos de esta colección.", "加载样本或运行查询以建议此集合的字段。")
        , ("databasesLoaded", "{0} database(s) loaded.", "{0} banco(s) carregado(s).", "{0} base(s) de datos cargada(s).", "已加载 {0} 个数据库。")
        , ("collectionsLoaded", "{0} collection(s) loaded in {1}.", "{0} coleção(ões) carregada(s) em {1}.", "{0} colección(es) cargada(s) en {1}.", "已在 {1} 中加载 {0} 个集合。")
        , ("auditNotRecorded", "Action completed, but local audit was not recorded: {0}", "Ação concluída, mas a auditoria local não foi registrada: {0}", "La acción se completó, pero no se registró la auditoría local: {0}", "操作已完成，但未记录本地审计：{0}")
        , ("operationInProgress", "Operation in progress…", "Operação em andamento…", "Operación en curso…", "操作进行中…")
        , ("operationCancelledByUser", "Operation canceled by the user.", "Operação cancelada pelo usuário.", "Operación cancelada por el usuario.", "操作已被用户取消。")
        , ("operationNotReverted", "Effects already sent to MongoDB are not reverted automatically.", "Efeitos já enviados ao MongoDB não são revertidos automaticamente.", "Los efectos ya enviados a MongoDB no se revierten automáticamente.", "已发送到 MongoDB 的效果不会自动回滚。")
        , ("operationNotCompleted", "The operation was not completed. See the message above.", "A operação não foi concluída. Consulte a mensagem acima.", "La operación no se completó. Consulte el mensaje anterior.", "操作未完成。请查看上面的消息。")
        , ("errorPrefix", "Error: {0}", "Erro: {0}", "Error: {0}", "错误：{0}")
        , ("filterReset", "Filter set to {}. The next query starts at the first document.", "Filtro definido como {}. A próxima consulta começa no primeiro documento.", "Filtro establecido en {}. La próxima consulta comienza en el primer documento.", "过滤器已设为 {}。下一次查询从第一个文档开始。")
        , ("queryInitialResults", "Select a connection, load databases and run a query.", "Selecione uma conexão, carregue os bancos e execute uma consulta.", "Seleccione una conexión, cargue las bases de datos y ejecute una consulta.", "选择连接、加载数据库并运行查询。")
        , ("exactCountNote", "Exact count respects the filter; estimated count considers the entire collection.", "A contagem exata respeita o filtro; a estimada considera a coleção inteira.", "El conteo exacto respeta el filtro; el estimado considera toda la colección.", "精确计数遵循过滤器；估算计数考虑整个集合。")
        , ("distinctInitial", "Enter a field and find distinct values using the current filter.", "Informe um campo e execute valores distintos usando o filtro atual.", "Indique un campo y busque valores distintos usando el filtro actual.", "输入字段并使用当前过滤器查找不同值。")
        , ("explainInitial", "Run Explain to view the query plan and statistics.", "Execute Explain para visualizar o plano e as estatísticas da consulta.", "Ejecute Explain para ver el plan y las estadísticas de la consulta.", "运行 Explain 查看查询计划和统计信息。")
        , ("aggregationInitial", "Select a collection and run an aggregation pipeline.", "Selecione uma coleção e execute um pipeline de agregação.", "Seleccione una colección y ejecute un pipeline de agregación.", "选择集合并运行聚合管道。")
        , ("queryReturned", "{0} document(s) returned in {1} ms.", "{0} documento(s) retornado(s) em {1} ms.", "{0} documento(s) devuelto(s) en {1} ms.", "返回 {0} 个文档，用时 {1} 毫秒。")
        , ("limitReached", " Limit reached.", " Limite atingido.", " Límite alcanzado.", " 已达到限制。")
        , ("exportedExtendedJson", "{0} document(s) exported as Extended JSON.", "{0} documento(s) exportado(s) em Extended JSON.", "{0} documento(s) exportado(s) como Extended JSON.", "已将 {0} 个文档导出为 Extended JSON。")
        , ("newJsonFileRequired", "Provide a new file with the .json extension.", "Informe um arquivo novo com extensão .json.", "Indique un archivo nuevo con extensión .json.", "请提供扩展名为 .json 的新文件。")
        , ("estimatedCount", "Estimated collection count: {0} document(s) in {1} ms. Does not use a filter.", "Estimativa da coleção: {0} documento(s) em {1} ms. Não usa filtro.", "Conteo estimado de la colección: {0} documento(s) en {1} ms. No usa filtro.", "集合估算计数：{0} 个文档，用时 {1} 毫秒。不使用过滤器。")
        , ("exactCountResult", "Exact filtered count: {0} document(s) in {1} ms.", "Contagem exata do filtro: {0} documento(s) em {1} ms.", "Conteo exacto del filtro: {0} documento(s) en {1} ms.", "过滤器精确计数：{0} 个文档，用时 {1} 毫秒。")
        , ("estimatedCountDone", "Estimated collection count completed.", "Estimativa da coleção concluída.", "Conteo estimado de la colección completado.", "集合估算计数已完成。")
        , ("exactCountDone", "Exact count completed.", "Contagem exata concluída.", "Conteo exacto completado.", "精确计数已完成。")
        , ("distinctNone", "No distinct value found.", "Nenhum valor distinto encontrado.", "No se encontraron valores distintos.", "未找到不同值。")
        , ("distinctReturned", "{0} distinct value(s) returned in {1} ms.", "{0} valor(es) distinto(s) retornado(s) em {1} ms.", "{0} valor(es) distinto(s) devuelto(s) en {1} ms.", "返回 {0} 个不同值，用时 {1} 毫秒。")
        , ("explainLoaded", "Explain plan loaded.", "Plano Explain carregado.", "Plan Explain cargado.", "Explain 计划已加载。")
        , ("pipelineNone", "The pipeline returned no documents.", "O pipeline não retornou documentos.", "El pipeline no devolvió documentos.", "管道未返回文档。")
        , ("pipelineReturned", "Pipeline returned {0} document(s) in {1} ms.", "Pipeline retornou {0} documento(s) em {1} ms.", "El pipeline devolvió {0} documento(s) en {1} ms.", "管道返回 {0} 个文档，用时 {1} 毫秒。")
        , ("documentPreviewInitial", "Enter a filter and load a preview before changing a document.", "Informe um filtro e carregue uma prévia antes de alterar um documento.", "Indique un filtro y cargue una vista previa antes de cambiar un documento.", "输入过滤器并加载预览后再修改文档。")
        , ("findModifyInitial", "The document returned after the change will appear here.", "O documento retornado após a alteração aparecerá aqui.", "El documento devuelto después del cambio aparecerá aquí.", "更改后返回的文档将显示在这里。")
        , ("insertedDocument", "Document inserted.", "Documento inserido.", "Documento insertado.", "文档已插入。")
        , ("previewNone", "No document matches the current filter.", "Nenhum documento corresponde ao filtro atual.", "Ningún documento coincide con el filtro actual.", "没有文档匹配当前过滤器。")
        , ("previewNotFound", "Preview found no document.", "Prévia não encontrou documento.", "La vista previa no encontró ningún documento.", "预览未找到文档。")
        , ("previewLoaded", "Preview loaded; review the document before replacing or updating fields.", "Prévia carregada; revise o documento antes de substituir ou atualizar campos.", "Vista previa cargada; revise el documento antes de reemplazarlo o actualizar campos.", "预览已加载；替换或更新字段前请检查文档。")
        , ("duplicateDraft", "Copy draft prepared without _id. Review it and use Insert to create the new document.", "Rascunho de cópia preparado sem o _id. Revise e use Inserir para criar o novo documento.", "Borrador de copia preparado sin _id. Revíselo y use Insertar para crear el nuevo documento.", "已准备不含 _id 的副本文稿。请检查并使用插入创建新文档。")
        , ("bulkInsertDone", "Bulk insertion completed: {0} document(s).", "Inserção em lote concluída: {0} documento(s).", "Inserción por lotes completada: {0} documento(s).", "批量插入已完成：{0} 个文档。")
        , ("replaceDone", "Replacement completed: {0} document(s) found, {1} modified.", "Substituição concluída: {0} documento(s) encontrado(s), {1} modificado(s).", "Reemplazo completado: {0} documento(s) encontrado(s), {1} modificado(s).", "替换已完成：找到 {0} 个文档，修改 {1} 个。")
        , ("partialUpdateDone", "Partial update completed: {0} found, {1} modified.", "Atualização parcial concluída: {0} encontrado(s), {1} modificado(s).", "Actualización parcial completada: {0} encontrado(s), {1} modificado(s).", "部分更新已完成：找到 {0} 个，修改 {1} 个。")
        , ("upsertedId", " Upserted: {0}.", " Upsertado: {0}.", " Upsert realizado: {0}.", " 已 upsert：{0}。")
        , ("findModifyNone", "No document matched the filter.", "Nenhum documento correspondeu ao filtro.", "Ningún documento coincidió con el filtro.", "没有文档匹配过滤器。")
        , ("findModifyDone", "Find-and-modify completed; the document after the change was returned.", "Find-and-modify concluído; o documento após a alteração foi retornado.", "Find-and-modify completado; se devolvió el documento después del cambio.", "查找并修改已完成；已返回更改后的文档。")
        , ("deleteManyDone", "Bulk deletion completed: {0} document(s) removed.", "Exclusão em lote concluída: {0} documento(s) removido(s).", "Eliminación por lotes completada: {0} documento(s) eliminado(s).", "批量删除已完成：已删除 {0} 个文档。")
        , ("deleteOneDone", "Document deletion completed: {0} document(s) removed.", "Exclusão de um documento concluída: {0} documento(s) removido(s).", "Eliminación de documento completada: {0} documento(s) eliminado(s).", "文档删除已完成：已删除 {0} 个文档。")
        , ("adminInitial", "Load server status or statistics for the selected database.", "Carregue o status do servidor ou as estatísticas do banco selecionado.", "Cargue el estado del servidor o las estadísticas de la base seleccionada.", "加载服务器状态或所选数据库的统计信息。")
        , ("serverStatusLoaded", "Server status loaded.", "Status do servidor carregado.", "Estado del servidor cargado.", "服务器状态已加载。")
        , ("currentOperationsLoaded", "Current operations loaded.", "Operações correntes carregadas.", "Operaciones actuales cargadas.", "当前操作已加载。")
        , ("profilerLoaded", "Current profiler configuration loaded.", "Configuração atual do profiler carregada.", "Configuración actual del profiler cargada.", "当前分析器配置已加载。")
        , ("topologyLoaded", "MongoDB topology loaded.", "Topologia MongoDB carregada.", "Topología de MongoDB cargada.", "MongoDB 拓扑已加载。")
        , ("killRequested", "Interruption requested for operation {0}.", "Interrupção solicitada para a operação {0}.", "Interrupción solicitada para la operación {0}.", "已请求中断操作 {0}。")
        , ("profilesLoaded", "{0} connection(s) loaded.", "{0} conexão(ões) carregada(s).", "{0} conexión(es) cargada(s).", "已加载 {0} 个连接。")
        , ("profileFromUri", "Profile populated from the URI. Review the fields before saving.", "Perfil preenchido a partir da URI. Revise os campos antes de salvar.", "Perfil rellenado a partir de la URI. Revise los campos antes de guardar.", "已从 URI 填充配置。保存前请检查字段。")
        , ("duplicateProfileHint", "Review the name and save the connection copy.", "Revise o nome e salve a cópia da conexão.", "Revise el nombre y guarde la copia de la conexión.", "请检查名称并保存连接副本。")
        , ("profileRemoved", "Connection {0} removed from the LiteDB workspace.", "Conexão {0} removida do workspace LiteDB.", "Conexión {0} eliminada del workspace LiteDB.", "已从 LiteDB 工作区删除连接 {0}。")
        , ("profileSaved", "Connection saved to the LiteDB workspace.", "Conexão salva no workspace LiteDB.", "Conexión guardada en el workspace LiteDB.", "连接已保存到 LiteDB 工作区。")
        , ("profileUpdated", "Connection updated in the LiteDB workspace.", "Conexão atualizada no workspace LiteDB.", "Conexión actualizada en el workspace LiteDB.", "连接已在 LiteDB 工作区更新。")
        , ("profileCredentialCleanupPending", "The connection was saved, but cleanup of its previous credential is pending.", "A conexão foi salva, mas a limpeza da credencial anterior está pendente.", "La conexión se guardó, pero la limpieza de la credencial anterior está pendiente.", "连接已保存，但之前凭据的清理仍待完成。")
        , ("profileCredentialReentryRequired", "The URI changed. Enter the connection password again before saving.", "A URI foi alterada. Informe novamente a senha da conexão antes de salvar.", "La URI cambió. Vuelva a introducir la contraseña de la conexión antes de guardar.", "URI 已更改。保存前请重新输入连接密码。")
        , ("uuidPreferenceNotSaved", "UUID preference was not saved: {0}", "Preferência UUID não salva: {0}", "No se guardó la preferencia UUID: {0}", "UUID 偏好设置未保存：{0}")
        , ("connectedToMongo", "Connected to MongoDB {0} in {1} ms.", "Conectado a MongoDB {0} em {1} ms.", "Conectado a MongoDB {0} en {1} ms.", "已连接到 MongoDB {0}，用时 {1} 毫秒。")
        , ("versionUnknown", "version unknown", "versão não informada", "versión no informada", "版本未知")
        , ("connectionFailure", "Connection failed: {0}", "Falha ao conectar: {0}", "Falló la conexión: {0}", "连接失败：{0}")
        , ("invalidMongoUri", "The MongoDB URI is invalid.", "A URI MongoDB é inválida.", "La URI de MongoDB no es válida.", "MongoDB URI 无效。")
        , ("queryLoadedHistory", "Query loaded from local history.", "Consulta carregada do histórico local.", "Consulta cargada del historial local.", "已从本地历史记录加载查询。")
        , ("savedQueryLoaded", "Saved query loaded.", "Consulta salva carregada.", "Consulta guardada cargada.", "已加载保存的查询。")
        , ("savedQueryStored", "Query saved to the LiteDB workspace.", "Consulta salva no workspace LiteDB.", "Consulta guardada en el workspace LiteDB.", "查询已保存到 LiteDB 工作区。")
        , ("savedQueryRemoved", "Saved query '{0}' removed.", "Consulta salva '{0}' removida.", "Consulta guardada '{0}' eliminada.", "已删除保存的查询“{0}”。")
        , ("savedQueryNameHint", "Enter a name to save the current query.", "Informe um nome para salvar a consulta atual.", "Indique un nombre para guardar la consulta actual.", "输入名称以保存当前查询。")
        , ("indexesInitial", "Select a collection to list its indexes.", "Selecione uma coleção para listar seus índices.", "Seleccione una colección para listar sus índices.", "选择集合以列出索引。")
        , ("noIndexes", "No indexes found.", "Nenhum índice encontrado.", "No se encontraron índices.", "未找到索引。")
        , ("indexesLoaded", "{0} index(es) loaded.", "{0} índice(s) carregado(s).", "{0} índice(s) cargado(s).", "已加载 {0} 个索引。")
        , ("noIndexStats", "No index usage statistics returned by the server.", "Nenhuma estatística de uso retornada pelo servidor.", "El servidor no devolvió estadísticas de uso.", "服务器未返回索引使用统计信息。")
        , ("indexStatsLoaded", "{0} index usage statistic(s) loaded.", "{0} estatística(s) de uso de índice carregada(s).", "Se cargaron {0} estadística(s) de uso de índices.", "已加载 {0} 条索引使用统计信息。")
        , ("indexCreated", "Index {0} created.", "Índice {0} criado.", "Índice {0} creado.", "已创建索引 {0}。")
        , ("indexDropped", "Index {0} removed.", "Índice {0} removido.", "Índice {0} eliminado.", "已删除索引 {0}。")
        , ("indexVisibilityChanged", "Index {0} visibility changed.", "Visibilidade do índice {0} alterada.", "Visibilidad del índice {0} cambiada.", "索引 {0} 的可见性已更改。")
        , ("auditInitialNone", "No audited action in the local workspace.", "Nenhuma ação auditada no workspace local.", "No hay acciones auditadas en el workspace local.", "本地工作区没有审计操作。")
        , ("auditLoaded", "{0} local audit action(s) loaded.", "{0} ação(ões) locais de auditoria carregada(s).", "Se cargaron {0} acción(es) locales de auditoría.", "已加载 {0} 条本地审计操作。")
        , ("auditPathRequired", "Provide the JSON audit file path.", "Informe o caminho do arquivo JSON da auditoria.", "Indique la ruta del archivo JSON de auditoría.", "请提供审计 JSON 文件路径。")
        , ("auditExtension", "The audit file must use the .json extension.", "O arquivo de auditoria precisa usar a extensão .json.", "El archivo de auditoría debe usar la extensión .json.", "审计文件必须使用 .json 扩展名。")
        , ("auditExported", "Audit exported to {0}.", "Auditoria exportada para {0}.", "Auditoría exportada a {0}.", "审计已导出到 {0}。")
        , ("auditAlreadyExists", "The audit file already exists. Choose another path to avoid overwriting previous evidence.", "O arquivo de auditoria já existe. Escolha outro caminho para não sobrescrever a evidência anterior.", "El archivo de auditoría ya existe. Elija otra ruta para no sobrescribir la evidencia anterior.", "审计文件已存在。请选择其他路径，以免覆盖之前的证据。")
        , ("auditExportFailed", "Failed to export audit: {0}", "Falha ao exportar auditoria: {0}", "Falló la exportación de auditoría: {0}", "导出审计失败：{0}")
        , ("suggestMqlHint", "Contextual suggestions are in the editor: use Ctrl+. or Ctrl+Space.", "As sugestões contextuais ficam no editor: use Ctrl+. ou Ctrl+Espaço.", "Las sugerencias contextuales están en el editor: use Ctrl+. o Ctrl+Espacio.", "上下文建议位于编辑器中：使用 Ctrl+. 或 Ctrl+Space。")
        , ("applyMqlHint", "Apply suggestions directly in the editor with Ctrl+. or Ctrl+Space.", "Aplique sugestões diretamente no editor com Ctrl+. ou Ctrl+Espaço.", "Aplique sugerencias directamente en el editor con Ctrl+. o Ctrl+Espacio.", "使用 Ctrl+. 或 Ctrl+Space 直接在编辑器中应用建议。")
        , ("suggestAggregationHint", "Aggregation suggestions are in the editor: use Ctrl+. or Ctrl+Space.", "As sugestões de agregação ficam no editor: use Ctrl+. ou Ctrl+Espaço.", "Las sugerencias de agregación están en el editor: use Ctrl+. o Ctrl+Espacio.", "聚合建议位于编辑器中：使用 Ctrl+. 或 Ctrl+Space。")
        , ("noSuggestableFields", "The sample contains no fields that can be suggested.", "A amostra não contém campos que possam ser sugeridos.", "La muestra no contiene campos que puedan sugerirse.", "样本不包含可建议的字段。")
        , ("fieldsInferred", "{0} field(s) inferred from {1} document(s). Use Suggest MQL in the filter.", "{0} campo(s) inferido(s) de {1} documento(s). Use Sugerir MQL no filtro.", "Se infirieron {0} campo(s) de {1} documento(s). Use Sugerir MQL en el filtro.", "从 {1} 个文档推断出 {0} 个字段。请在过滤器中使用建议 MQL。")
        , ("schemaSampleLoaded", "Schema sample loaded: {0} document(s), {1} field(s).", "Amostra de schema carregada: {0} documento(s), {1} campo(s).", "Muestra de esquema cargada: {0} documento(s), {1} campo(s).", "架构样本已加载：{0} 个文档，{1} 个字段。")
        , ("validatorInferred", "Validator inferred from {0} document(s); review before applying.", "Validador inferido de {0} documento(s); revise antes de aplicar.", "Validador inferido de {0} documento(s); revise antes de aplicar.", "已从 {0} 个文档推断验证器；应用前请检查。")
        , ("scriptPathLoaded", "Script path loaded from local history.", "Caminho de script carregado do histórico local.", "Ruta del script cargada del historial local.", "已从本地历史记录加载脚本路径。")
        , ("scriptCompleted", "Script completed in {0} ms.", "Script concluído em {0} ms.", "Script completado en {0} ms.", "脚本已完成，用时 {0} 毫秒。")
        , ("scriptExitCode", "Script completed with code {0}.", "Script concluído com código {0}.", "Script completado con código {0}.", "脚本已完成，代码为 {0}。")
        , ("scriptSaved", "Script saved to {0}.", "Script salvo em {0}.", "Script guardado en {0}.", "脚本已保存到 {0}。")
        , ("scriptLoaded", "Script loaded from {0}.", "Script carregado de {0}.", "Script cargado desde {0}.", "脚本已从 {0} 加载。")
        , ("scriptResultsHeader", "RESULTS EJSON", "RESULTADOS EJSON", "RESULTADOS EJSON", "EJSON 结果")
        , ("scriptConsoleHeader", "CONSOLE", "CONSOLE", "CONSOLA", "控制台")
        , ("scriptErrorsHeader", "ERRORS", "ERROS", "ERRORES", "错误")
        , ("scriptNoOutput", "The script returned no results or messages.", "O script não retornou resultados nem mensagens.", "El script no devolvió resultados ni mensajes.", "脚本未返回结果或消息。")
        , ("exportDatabaseInitial", "Select a database to export it as Extended JSON.", "Selecione um banco para exportá-lo em Extended JSON.", "Seleccione una base de datos para exportarla como Extended JSON.", "选择数据库以将其导出为 Extended JSON。")
        , ("importDatabaseInitial", "Provide the export folder and choose the target database.", "Informe a pasta da exportação e escolha o banco de destino.", "Indique la carpeta de exportación y elija la base de datos de destino.", "提供导出文件夹并选择目标数据库。")
        , ("databaseExportSummary", "{0} collection(s), {1} document(s).", "{0} coleção(ões), {1} documento(s).", "{0} colección(es), {1} documento(s).", "{0} 个集合，{1} 个文档。")
        , ("folderLine", "Folder: {0}", "Pasta: {0}", "Carpeta: {0}", "文件夹：{0}")
        , ("manifestLine", "Manifest: manifest.json", "Manifesto: manifest.json", "Manifiesto: manifest.json", "清单：manifest.json")
        , ("exportLimitWarning", "Warning: at least one collection reached the configured limit.", "Atenção: ao menos uma coleção atingiu o limite configurado.", "Aviso: al menos una colección alcanzó el límite configurado.", "警告：至少有一个集合达到了配置的限制。")
        , ("databaseExported", "Export completed: {0} document(s).", "Exportação concluída: {0} documento(s).", "Exportación completada: {0} documento(s).", "导出已完成：{0} 个文档。")
        , ("databaseImportSummary", "{0} collection(s), {1} document(s) imported by _id upsert.\nSource: {2}\nTarget: {3}", "{0} coleção(ões), {1} documento(s) importado(s) por upsert de _id.\nOrigem: {2}\nDestino: {3}", "{0} colección(es), {1} documento(s) importado(s) mediante upsert de _id.\nOrigen: {2}\nDestino: {3}", "{0} 个集合，{1} 个文档通过 _id upsert 导入。\n来源：{2}\n目标：{3}")
        , ("databaseImported", "Import completed: {0} document(s).", "Importação concluída: {0} documento(s).", "Importación completada: {0} documento(s).", "导入已完成：{0} 个文档。")
        , ("usersLoaded", "MongoDB users loaded.", "Usuários MongoDB carregados.", "Usuarios de MongoDB cargados.", "MongoDB 用户已加载。")
        , ("rolesLoaded", "MongoDB roles loaded.", "Papéis MongoDB carregados.", "Roles de MongoDB cargados.", "MongoDB 角色已加载。")
        , ("userCreated", "User {0} created in database {1}.", "Usuário {0} criado no banco {1}.", "Usuario {0} creado en la base de datos {1}.", "已在数据库 {1} 中创建用户 {0}。")
        , ("userRemoved", "User {0} removed from database {1}.", "Usuário {0} removido do banco {1}.", "Usuario {0} eliminado de la base de datos {1}.", "已从数据库 {1} 删除用户 {0}。")
        , ("userCreatedAudit", "User {0} created.", "Usuário {0} criado.", "Usuario {0} creado.", "已创建用户 {0}。")
        , ("userRemovedAudit", "User {0} removed.", "Usuário {0} removido.", "Usuario {0} eliminado.", "已删除用户 {0}。")
        , ("rolesUpdated", "Roles for user {0} updated.", "Papéis do usuário {0} atualizados.", "Roles del usuario {0} actualizados.", "用户 {0} 的角色已更新。")
        , ("databaseStatsLoaded", "Statistics for {0} loaded.", "Estatísticas de {0} carregadas.", "Estadísticas de {0} cargadas.", "已加载 {0} 的统计信息。")
        , ("databaseRemoved", "Database {0} removed.", "Banco {0} removido.", "Base de datos {0} eliminada.", "已删除数据库 {0}。")
        , ("databaseCreated", "Database {0} created with collection {1}.", "Banco {0} criado com a coleção {1}.", "Base de datos {0} creada con la colección {1}.", "已创建数据库 {0}，集合为 {1}。")
        , ("collectionCreated", "Collection {0} created.", "Coleção {0} criada.", "Colección {0} creada.", "已创建集合 {0}。")
        , ("viewCreated", "View {0} created.", "View {0} criada.", "Vista {0} creada.", "已创建视图 {0}。")
        , ("collectionCreatedAudit", "Collection created.", "Coleção criada.", "Colección creada.", "已创建集合。")
        , ("viewCreatedAudit", "View created.", "View criada.", "Vista creada.", "已创建视图。")
        , ("collectionRenamedAudit", "Collection renamed from {0} to {1}.", "Coleção renomeada de {0} para {1}.", "Colección renombrada de {0} a {1}.", "集合已从 {0} 重命名为 {1}。")
        , ("collectionRenamed", "Collection {0} renamed to {1}.", "Coleção {0} renomeada para {1}.", "Colección {0} renombrada a {1}.", "集合 {0} 已重命名为 {1}。")
        , ("viewPipelineUpdatedAudit", "View pipeline updated.", "Pipeline da view atualizado.", "Pipeline de la vista actualizado.", "视图管道已更新。")
        , ("viewPipelineUpdated", "Pipeline for view {0} updated.", "Pipeline da view {0} atualizado.", "Pipeline de la vista {0} actualizado.", "视图 {0} 的管道已更新。")
        , ("validationConfiguredAudit", "Validation configured: level {0}, action {1}.", "Validação configurada: nível {0}, ação {1}.", "Validación configurada: nivel {0}, acción {1}.", "已配置验证：级别 {0}，操作 {1}。")
        , ("collectionValidationUpdated", "Validation for collection {0} updated.", "Validação da coleção {0} atualizada.", "Validación de la colección {0} actualizada.", "集合 {0} 的验证已更新。")
        , ("collectionValidationLoaded", "Current validation for collection {0} loaded.", "Validação atual da coleção {0} carregada.", "Validación actual de la colección {0} cargada.", "已加载集合 {0} 的当前验证。")
        , ("collectionRemovedAudit", "Collection removed.", "Coleção removida.", "Colección eliminada.", "已删除集合。")
        , ("collectionRemoved", "Collection {0} removed.", "Coleção {0} removida.", "Colección {0} eliminada.", "已删除集合 {0}。")
        , ("collectionValidationDone", "Validation for collection {0} completed.", "Validação da coleção {0} concluída.", "Validación de la colección {0} completada.", "集合 {0} 的验证已完成。")
        , ("collectionIntegrityAudit", "Collection integrity verified.", "Integridade da coleção verificada.", "Integridad de la colección verificada.", "已验证集合完整性。")
        , ("collectionCompacted", "Compaction for collection {0} completed.", "Compactação da coleção {0} concluída.", "Compactación de la colección {0} completada.", "集合 {0} 的压缩已完成。")
        , ("compactionRequested", "Compaction requested.", "Compactação solicitada.", "Compactación solicitada.", "已请求压缩。")
        , ("collectionStatsLoaded", "Statistics for collection {0} loaded.", "Estatísticas da coleção {0} carregadas.", "Estadísticas de la colección {0} cargadas.", "已加载集合 {0} 的统计信息。")
        , ("readOnlyDocumentStatus", "Read-only preview; nothing was executed.", "Visualização somente leitura; nada foi executado.", "Vista previa de solo lectura; no se ejecutó nada.", "只读预览；未执行任何操作。")
        , ("canonicalExtendedJson", " · export continues as canonical Extended JSON", " · exportação continua em Extended JSON canônico", " · la exportación continúa como Extended JSON canónico", " · 导出仍使用规范 Extended JSON")
        , ("legacyUuidOrigin", " · {0} legacy UUID(s) of unknown origin", " · {0} UUID(s) legado(s) de origem desconhecida", " · {0} UUID(s) heredado(s) de origen desconocido", " · {0} 个来源未知的旧版 UUID")
        , ("environmentActive", "Active environment: {0}", "Ambiente ativo: {0}", "Entorno activo: {0}", "当前环境：{0}")
        , ("loadEnvironmentsFailed", "Could not load environments: {0}", "Não foi possível carregar os ambientes: {0}", "No se pudieron cargar los entornos: {0}", "无法加载环境：{0}")
        , ("environmentNameRequired", "Provide a unique environment name with up to 60 characters.", "Informe um nome único de ambiente, com até 60 caracteres.", "Indique un nombre de entorno único de hasta 60 caracteres.", "请输入最多 60 个字符的唯一环境名称。")
        , ("environmentAndKeyRequired", "Select an environment and provide a key.", "Selecione um ambiente e informe uma chave.", "Seleccione un entorno e indique una clave.", "选择环境并提供键。")
        , ("environmentAdded", "Environment added to the form. Save to apply.", "Ambiente adicionado ao formulário. Salve para aplicar.", "Entorno añadido al formulario. Guarde para aplicar.", "环境已添加到表单。保存后应用。")
        , ("environmentVariableUpdated", "Variable updated in the form. Save to apply.", "Variável atualizada no formulário. Salve para aplicar.", "Variable actualizada en el formulario. Guarde para aplicar.", "变量已在表单中更新。保存后应用。")
        , ("environmentVariableRemoved", "Variable removed from the form. Save to apply.", "Variável removida do formulário. Salve para aplicar.", "Variable eliminada del formulario. Guarde para aplicar.", "变量已从表单中删除。保存后应用。")
        , ("environmentPendingVariable", "Add the variable being edited before saving.", "Adicione a variável em edição antes de salvar.", "Añada la variable que está editando antes de guardar.", "保存前请添加正在编辑的变量。")
        , ("environmentReopenConnections", "Active environment: {0}. Reopen connections to update the target.", "Ambiente ativo: {0}. Reabra as conexões para atualizar o destino.", "Entorno activo: {0}. Reabra las conexiones para actualizar el destino.", "当前环境：{0}。重新打开连接以更新目标。")
        , ("environmentsNotSaved", "Environments were not saved: {0}", "Ambientes não salvos: {0}", "No se guardaron los entornos: {0}", "环境未保存：{0}")
        , ("uuidPreferenceSaved", "UUID preference saved. Open results were updated.", "Preferência de UUID salva. Resultados abertos foram atualizados.", "Preferencia de UUID guardada. Se actualizaron los resultados abiertos.", "UUID 偏好设置已保存。已更新打开的结果。")
        , ("uuidPreferenceSessionOnly", "Preference applied in this session but not saved: {0}", "Preferência aplicada nesta sessão, mas não salva: {0}", "Preferencia aplicada en esta sesión, pero no guardada: {0}", "偏好设置已应用于本会话但未保存：{0}")
        , ("explorerOpenConnection", "Open a connection to explore databases.", "Abra uma conexão para explorar os bancos.", "Abra una conexión para explorar las bases de datos.", "打开连接以浏览数据库。")
        , ("environmentUpdatedReopen", "Environment updated. Reopen connections to check the target.", "Ambiente atualizado. Reabra as conexões para conferir o destino.", "Entorno actualizado. Reabra las conexiones para comprobar el destino.", "环境已更新。重新打开连接以检查目标。")
        , ("profileChangedReopen", "Profile changed or removed — open the connection again to update the target.", "Perfil alterado ou removido — abra novamente a conexão para atualizar o destino.", "Perfil cambiado o eliminado: vuelva a abrir la conexión para actualizar el destino.", "配置已更改或删除 — 再次打开连接以更新目标。")
        , ("targetChangedReopen", "The target changed while opening. Open the connection again.", "O destino mudou durante a abertura. Abra novamente a conexão.", "El destino cambió durante la apertura. Abra la conexión de nuevo.", "打开时目标发生变化。请再次打开连接。")
        , ("connectionNotOpen", "Connection is not open.", "Conexão não aberta.", "La conexión no está abierta.", "连接未打开。")
        , ("explorerDatabasesLoaded", "{0} database(s) · collections load when expanded", "{0} banco(s) · coleções carregadas ao expandir", "{0} base(s) de datos · las colecciones se cargan al expandir", "{0} 个数据库 · 展开时加载集合")
        , ("connectionDisconnected", "Connection disconnected. Running operations keep their own context and cancellation.", "Conexão desconectada. Operações em andamento conservam seu contexto e cancelamento próprios.", "Conexión desconectada. Las operaciones en curso conservan su propio contexto y cancelación.", "连接已断开。正在运行的操作保留自己的上下文和取消状态。")
        , ("profileRemovedReopen", "Profile removed. Open the connections again.", "Perfil removido. Abra novamente as conexões.", "Perfil eliminado. Abra las conexiones de nuevo.", "配置已删除。请重新打开连接。")
        , ("openIndexRequired", "Select an index from an open connection.", "Selecione um índice de uma conexão aberta.", "Seleccione un índice de una conexión abierta.", "请从打开的连接中选择索引。")
        , ("indexRemovedExplorer", "Index removed: {0}", "Índice removido: {0}", "Índice eliminado: {0}", "已删除索引：{0}")
        , ("loadedSearchLimited", "Search is limited to already loaded items.", "Busca limitada aos itens já carregados.", "La búsqueda se limita a los elementos ya cargados.", "搜索仅限于已加载项目。")
        , ("loadedSearchNone", "No loaded item matches the search.", "Nenhum item carregado corresponde à busca.", "Ningún elemento cargado coincide con la búsqueda.", "没有已加载项目匹配搜索。")
        , ("expandDatabaseCollections", "Expand a database to load collections.", "Expanda um banco para carregar coleções.", "Expanda una base de datos para cargar colecciones.", "展开数据库以加载集合。")
        , ("historyEnvironment", "History from {0}; a new execution will use the current active environment.", "Histórico de {0}; uma nova execução usará o ambiente ativo atual.", "Historial de {0}; una nueva ejecución usará el entorno activo actual.", "来自 {0} 的历史记录；新的执行将使用当前活动环境。")
        , ("editorDocumentRecoveryOff", "Document in editor; automatic recovery is disabled for this tab. Use Save to write a file explicitly.", "Documento no editor; recuperação automática desativada para esta aba. Use Salvar para gravar um arquivo explicitamente.", "Documento en el editor; la recuperación automática está desactivada para esta pestaña. Use Guardar para escribir un archivo explícitamente.", "文档位于编辑器中；此标签页已禁用自动恢复。请使用保存显式写入文件。")
        , ("waitOperationClose", "Wait for the operation to finish before closing the tab.", "Aguarde o encerramento da operação antes de fechar a aba.", "Espere a que termine la operación antes de cerrar la pestaña.", "关闭标签页前请等待操作完成。")
        , ("newDocument", "New document", "Novo documento", "Nuevo documento", "新文档")
        , ("mutationEditLabel", "Save…", "Salvar…", "Guardar…", "保存…")
        , ("mutationDeleteLabel", "Delete…", "Excluir…", "Eliminar…", "删除…")
        , ("mutationReadOnlyPolicy", "Read-only: this connection blocks writes. The copy can be reviewed but not saved.", "Somente leitura: esta conexão bloqueia gravações. A cópia pode ser revisada, mas não salva.", "Solo lectura: esta conexión bloquea las escrituras. La copia puede revisarse, pero no guardarse.", "只读：此连接禁止写入。可以检查副本，但不能保存。")
        , ("mutationRereadPolicy", "Editable copy of the result; nothing was read or written when it opened. Saving requires confirmation, rereads by _id and uses a concurrent-change precondition.", "Cópia editável do resultado; nada foi lido ou gravado ao abrir. Salvar exige confirmação, relê o documento por _id e usa precondição contra alterações concorrentes.", "Copia editable del resultado; no se leyó ni escribió nada al abrir. Guardar requiere confirmación, vuelve a leer por _id y usa una precondición contra cambios concurrentes.", "结果的可编辑副本；打开时未读取或写入任何内容。保存需要确认，会按 _id 重新读取并使用并发更改前置条件。")
        , ("mutationWritePolicy", "Writing requires confirmation; the precondition rejects a document changed or removed after reading.", "Gravação exige confirmação; a precondição rejeita um documento alterado ou removido após a leitura.", "La escritura requiere confirmación; la precondición rechaza un documento cambiado o eliminado después de la lectura.", "写入需要确认；前置条件会拒绝读取后已更改或删除的文档。")
        , ("mutationReadOnlyBlocked", "Read-only connection: writing is blocked.", "Conexão somente leitura: gravação bloqueada.", "Conexión de solo lectura: escritura bloqueada.", "只读连接：写入被阻止。")
        , ("mutationReviewConfirm", "Review the document and target before confirming.", "Revise o documento e o destino antes de confirmar.", "Revise el documento y el destino antes de confirmar.", "确认前请检查文档和目标。")
        , ("mutationRereading", "Rereading the document before writing…", "Relendo o documento antes de gravar…", "Releyendo el documento antes de escribir…", "写入前正在重新读取文档…")
        , ("mutationRemovedAfterRead", "The document was removed after reading. Nothing was written; run the query again.", "O documento foi removido depois da leitura. Nada foi gravado; execute a consulta novamente.", "El documento se eliminó después de la lectura. No se escribió nada; ejecute la consulta de nuevo.", "文档在读取后被删除。未写入任何内容；请重新运行查询。")
        , ("mutationChangedAfterRead", "The document changed on the server after reading. Nothing was written; run the query again and review the copy.", "O documento foi alterado no servidor depois da leitura. Nada foi gravado; execute a consulta novamente e revise a cópia.", "El documento cambió en el servidor después de la lectura. No se escribió nada; ejecute la consulta de nuevo y revise la copia.", "文档在读取后在服务器上发生了更改。未写入任何内容；请重新运行查询并检查副本。")
        , ("unknownDocumentOperation", "Unknown document operation.", "Operação de documento desconhecida.", "Operación de documento desconocida.", "未知文档操作。")
        , ("mutationCompletedRefresh", "Operation completed. Refresh the page to query the current state.", "Operação concluída. Atualize a página para consultar o estado atual.", "Operación completada. Actualice la página para consultar el estado actual.", "操作已完成。刷新页面以查询当前状态。")
        , ("mutationConflict", "The document changed or was removed after reading. Refresh the page before trying again.", "O documento mudou ou foi removido após a leitura. Atualize a página antes de tentar novamente.", "El documento cambió o se eliminó después de la lectura. Actualice la página antes de volver a intentarlo.", "文档在读取后发生更改或被删除。请刷新页面后重试。")
        , ("mutationCancelled", "Canceled. Effects sent to the server are not reverted; check the result.", "Cancelado. Efeitos enviados ao servidor não são revertidos; confira o resultado.", "Cancelado. Los efectos enviados al servidor no se revierten; compruebe el resultado.", "已取消。发送到服务器的效果不会回滚；请检查结果。")
        , ("failurePrefix", "Failure: {0}", "Falha: {0}", "Fallo: {0}", "失败：{0}")
        , ("modelHardwareNotQueried", "Hardware has not been queried yet.", "Hardware ainda não consultado.", "El hardware aún no se ha consultado.", "尚未查询硬件。")
        , ("noModelBasicAvailable", "No model loaded. Basic autocomplete is available.", "Nenhum modelo carregado. Autocomplete básico disponível.", "No hay ningún modelo cargado. El autocompletado básico está disponible.", "未加载模型。基本自动补全可用。")
        , ("invalidModelPath", "Invalid model path.", "Caminho de modelo inválido.", "Ruta de modelo no válida.", "模型路径无效。")
        , ("externalModelValid", "External model is valid: {0}. Save to use it.", "Modelo externo válido: {0}. Salve para usar.", "Modelo externo válido: {0}. Guarde para usarlo.", "外部模型有效：{0}。保存后使用。")
        , ("externalFolderInvalid", "External folder cannot be used: {0}", "Pasta externa não utilizável: {0}", "La carpeta externa no se puede utilizar: {0}", "无法使用外部文件夹：{0}")
        , ("validateExternalFailed", "Could not validate the external folder.", "Não foi possível validar a pasta externa.", "No se pudo validar la carpeta externa.", "无法验证外部文件夹。")
        , ("searchingModels", "Searching for models in {0}…", "Procurando modelos em {0}…", "Buscando modelos en {0}…", "正在 {0} 中搜索模型…")
        , ("modelNotFound", "Model {0} was not found in {1}.", "Modelo {0} não encontrado em {1}.", "No se encontró el modelo {0} en {1}.", "在 {1} 中未找到模型 {0}。")
        , ("ignoredFolders", "Ignored folders: {0}", "Pastas ignoradas: {0}", "Carpetas ignoradas: {0}", "已忽略的文件夹：{0}")
        , ("modelsDirectoryMissing", "Model directory not found: {0}", "Diretório de modelos não encontrado: {0}", "Directorio de modelos no encontrado: {0}", "未找到模型目录：{0}")
        , ("modelsFound", "{0} model(s) found", "{0} modelo(s) encontrado(s)", "Se encontraron {0} modelo(s)", "找到 {0} 个模型")
        , ("ignoredFoldersCount", "; {0} folder(s) ignored.", "; {0} pasta(s) ignorada(s).", "; se ignoraron {0} carpeta(s).", "；已忽略 {0} 个文件夹。")
        , ("readModelsFailed", "Could not read the model directory. Check permissions and path.", "Não foi possível ler o diretório de modelos. Confira permissões e caminho.", "No se pudo leer el directorio de modelos. Compruebe los permisos y la ruta.", "无法读取模型目录。请检查权限和路径。")
        , ("hardwareDetectionUnavailable", "Hardware detection is unavailable in this composition.", "Detecção de hardware indisponível nesta composição.", "La detección de hardware no está disponible en esta composición.", "此组合中硬件检测不可用。")
        , ("onnxRuntimeFailed", "Could not query ONNX Runtime. CPU remains available.", "Não foi possível consultar o ONNX Runtime. CPU permanece disponível.", "No se pudo consultar ONNX Runtime. La CPU sigue disponible.", "无法查询 ONNX Runtime。CPU 仍可用。")
        , ("autocompleteSaved", "Autocomplete preferences saved.", "Preferências de autocomplete salvas.", "Preferencias de autocompletado guardadas.", "自动补全偏好设置已保存。")
        , ("autocompleteNotSaved", "Preferences were not saved. Check the values and local session state.", "Preferências não salvas. Confira os valores e o estado da sessão local.", "No se guardaron las preferencias. Compruebe los valores y el estado de la sesión local.", "偏好设置未保存。请检查值和本地会话状态。")
        , ("testingModel", "Testing model: folder, tokenizer, ONNX session, provider and generation…", "Testando modelo: pasta, tokenizer, sessão ONNX, provider e geração…", "Probando modelo: carpeta, tokenizer, sesión ONNX, provider y generación…", "正在测试模型：文件夹、tokenizer、ONNX 会话、provider 和生成…")
        , ("testCancelled", "Test canceled.", "Teste cancelado.", "Prueba cancelada.", "测试已取消。")
        , ("testIncomplete", "Test not completed. Check configuration and session persistence.", "Teste não concluído. Confira a configuração e a persistência da sessão.", "Prueba no completada. Compruebe la configuración y la persistencia de la sesión.", "测试未完成。请检查配置和会话持久性。")
        , ("modelsDirectoryUndefined", "Model directory is not defined.", "Diretório de modelos não definido.", "El directorio de modelos no está definido.", "未定义模型目录。")
        , ("createDirectoryFailed", "Could not create {0}: {1}", "Não foi possível criar {0}: {1}", "No se pudo crear {0}: {1}", "无法创建 {0}：{1}")
        , ("remoteModelsLoading", "Loading models from Hugging Face…", "Carregando modelos do Hugging Face…", "Cargando modelos de Hugging Face…", "正在从 Hugging Face 加载模型…")
        , ("remoteModelsNone", "No model published in the repositories", "Nenhum modelo publicado nos repositórios", "No hay modelos publicados en los repositorios", "存储库中没有已发布模型")
        , ("remoteModelsChoose", "Choose a model to download", "Escolha um modelo para baixar", "Elija un modelo para descargar", "选择要下载的模型")
        , ("remoteModelsNotLoaded", "Model list not loaded", "Lista de modelos não carregada", "Lista de modelos no cargada", "模型列表未加载")
        , ("remoteModelsUnavailable", "List unavailable. Check the connection and use Refresh list.", "Lista indisponível. Confira a conexão e use Atualizar lista.", "Lista no disponible. Compruebe la conexión y use Actualizar lista.", "列表不可用。请检查连接并使用刷新列表。")
        , ("remoteSourceNotice", "Source: Hugging Face ({0}), ONNX versions of SlopCoder-Mongo models. Downloads accept the license published in the repository. Downloads continue if this window closes; follow or cancel them in the bottom bar.", "Fonte: Hugging Face ({0}), versões ONNX dos modelos SlopCoder-Mongo. Ao baixar, você aceita a licença publicada no repositório. O download continua se esta janela for fechada; acompanhe ou cancele na barra inferior.", "Fuente: Hugging Face ({0}), versiones ONNX de los modelos SlopCoder-Mongo. Al descargar, acepta la licencia publicada en el repositorio. La descarga continúa si se cierra esta ventana; sígala o cancélela en la barra inferior.", "来源：Hugging Face（{0}），SlopCoder-Mongo 模型的 ONNX 版本。下载即表示接受存储库中发布的许可证。关闭此窗口后下载仍会继续；可在底部栏跟踪或取消。")
        , ("remoteHintCpuCompact", "Default on CPU: smaller and faster", "Padrão em CPU: menor e mais rápido", "Predeterminado en CPU: más pequeño y rápido", "CPU 默认：更小、更快")
        , ("remoteHintCpuQuality", "CPU: somewhat better autocomplete, more memory", "CPU: autocomplete um pouco melhor, mais memória", "CPU: autocompletado algo mejor, más memoria", "CPU：自动补全稍好，占用更多内存")
        , ("remoteHintGpuQuality", "Default with GPU: better quality and lower latency", "Padrão com GPU: melhor qualidade e menor latência", "Predeterminado con GPU: mejor calidad y menor latencia", "GPU 默认：质量更高、延迟更低")
        , ("remoteHintGpuLimited", "GPU with little free memory", "GPU com pouca memória livre", "GPU con poca memoria libre", "可用显存较少的 GPU")
        , ("remoteHintRecommendedCpuRam", "Recommended on CPU: better for the local AI Agent, about 3.8 GB RAM", "Recomendado em CPU: melhor no Agente IA local, cerca de 3,8 GB de RAM", "Recomendado en CPU: mejor para el Agente IA local, unos 3,8 GB de RAM", "推荐用于 CPU：更适合本地 AI 智能体，约需 3.8 GB 内存")
        , ("remoteHintCpuMemory", "CPU with little memory: lower accuracy for open-ended requests", "CPU com pouca memória: perde precisão em pedidos livres", "CPU con poca memoria: menor precisión en solicitudes abiertas", "内存较少的 CPU：开放式请求的准确度较低")
        , ("remoteHintRecommendedGpu", "Recommended with GPU: original model accuracy and low latency", "Recomendado com GPU: precisão do modelo original e baixa latência", "Recomendado con GPU: precisión del modelo original y baja latencia", "推荐用于 GPU：原始模型精度、低延迟")
        , ("remoteHintGpuMemory", "GPU with less free memory: more tokens per second", "GPU com menos memória livre: mais tokens por segundo", "GPU con menos memoria libre: más tokens por segundo", "可用显存较少的 GPU：每秒生成更多 token")
        , ("remoteInvalidModelDescription", "Invalid model description.", "Descrição de modelo inválida.", "Descripción de modelo no válida.", "模型描述无效。")
        , ("remoteModelAlreadyInstalled", "The model is already installed at {0}. Remove the folder to download it again.", "O modelo já está instalado em {0}. Remova a pasta para baixar novamente.", "El modelo ya está instalado en {0}. Elimine la carpeta para volver a descargarlo.", "模型已安装到 {0}。删除该文件夹后才能重新下载。")
        , ("remoteInvalidRevision", "The repository did not provide a valid revision.", "O repositório não informou uma revisão válida.", "El repositorio no proporcionó una revisión válida.", "存储库未提供有效版本。")
        , ("remoteListTimedOut", "Hugging Face did not respond in time for {0}.", "O Hugging Face não respondeu a tempo para {0}.", "Hugging Face no respondió a tiempo para {0}.", "Hugging Face 未及时响应 {0}。")
        , ("remoteUnexpectedResponse", "Unexpected response from Hugging Face for {0}.", "Resposta inesperada do Hugging Face para {0}.", "Respuesta inesperada de Hugging Face para {0}.", "Hugging Face 对 {0} 返回了意外响应。")
        , ("remoteInvalidHash", "Invalid hash for {0}.", "Hash inválido para {0}.", "Hash no válido para {0}.", "{0} 的哈希无效。")
        , ("remoteInvalidFileSize", "Invalid size for {0}.", "Tamanho inválido para {0}.", "Tamaño no válido para {0}.", "{0} 的大小无效。")
        , ("remoteDownloadTimedOut", "The download of {0} stopped responding.", "O download de {0} parou de responder.", "La descarga de {0} dejó de responder.", "{0} 的下载停止响应。")
        , ("remoteFileTooLarge", "{0} is larger than the published size and was discarded.", "{0} é maior que o tamanho publicado e foi descartado.", "{0} supera el tamaño publicado y se descartó.", "{0} 大于发布的大小，已丢弃。")
        , ("remoteFileHashMismatch", "{0} does not match the repository hash and was discarded.", "{0} não confere com o hash publicado no repositório e foi descartado.", "{0} no coincide con el hash publicado en el repositorio y se descartó.", "{0} 与存储库中发布的哈希不匹配，已丢弃。")
        , ("remoteInsufficientSpace", "Insufficient space in {0}: {1:N0} MB are required.", "Espaço insuficiente em {0}: são necessários {1:N0} MB.", "Espacio insuficiente en {0}: se necesitan {1:N0} MB.", "{0} 中的空间不足：需要 {1:N0} MB。")
        , ("downloadModelTo", "Downloading {0} to {1}…", "Baixando {0} para {1}…", "Descargando {0} en {1}…", "正在将 {0} 下载到 {1}…")
        , ("downloadModelProgress", "Downloading {0}: {1} of {2}.", "Baixando {0}: {1} de {2}.", "Descargando {0}: {1} de {2}.", "正在下载 {0}：{1}/{2}。")
        , ("modelDownloadedOperation", "Model {0} downloaded", "Modelo {0} baixado", "Modelo {0} descargado", "模型 {0} 已下载")
        , ("modelInstalledSave", "{0} installed at {1} and selected. Click Save to use it.", "{0} instalado em {1} e selecionado. Clique em Salvar para usá-lo.", "{0} instalado en {1} y seleccionado. Haga clic en Guardar para usarlo.", "{0} 已安装到 {1} 并选中。点击保存以使用。")
        , ("modelCatalogMissed", "Model downloaded to {0}, but the catalog did not recognize it. Check ignored folders.", "Modelo baixado em {0}, mas o catálogo não o reconheceu. Confira as pastas ignoradas.", "Modelo descargado en {0}, pero el catálogo no lo reconoció. Compruebe las carpetas ignoradas.", "模型已下载到 {0}，但目录未识别它。请检查已忽略的文件夹。")
        , ("modelDownloadCancelled", "Model download canceled", "Download de modelo cancelado", "Descarga de modelo cancelada", "模型下载已取消")
        , ("downloadCancelledResume", "Download canceled. Verified files are kept to resume on the next attempt.", "Download cancelado. Arquivos já verificados ficam guardados para retomar na próxima tentativa.", "Descarga cancelada. Los archivos verificados se conservan para reanudar en el próximo intento.", "下载已取消。已验证的文件会保留，以便下次尝试时继续。")
        , ("modelNotDownloaded", "Model not downloaded: {0}", "Modelo não baixado: {0}", "Modelo no descargado: {0}", "模型未下载：{0}")
        , ("downloadIncomplete", "Download not completed: {0}", "Download não concluído: {0}", "Descarga no completada: {0}", "下载未完成：{0}")
        , ("updateDownloading", "Downloading {0}%", "Baixando {0}%", "Descargando {0}%", "正在下载 {0}%")
        , ("updateRestart", "Restart", "Reiniciar", "Reiniciar", "重新启动")
        , ("updateAvailableManual", "Version {0} is available. The program folder is not writable: click to open the release page.", "Versão {0} disponível. A pasta do programa não permite escrita: clique para abrir a página do release.", "La versión {0} está disponible. La carpeta del programa no permite escritura: haga clic para abrir la página del lanzamiento.", "版本 {0} 可用。程序文件夹不可写：点击打开发布页面。")
        , ("updateAvailable", "Version {0} is available (current {1}). Click to download; installation occurs when closing.", "Versão {0} disponível (atual {1}). Clique para baixar; a instalação ocorre ao fechar.", "La versión {0} está disponible (actual {1}). Haga clic para descargar; se instalará al cerrar.", "版本 {0} 可用（当前为 {1}）。点击下载；关闭时安装。")
        , ("updateDownloadTip", "Downloading version {0}. Use Cancel in the status bar to interrupt.", "Baixando a versão {0}. Use Cancelar na barra de status para interromper.", "Descargando la versión {0}. Use Cancelar en la barra de estado para interrumpir.", "正在下载版本 {0}。使用状态栏中的取消来中断。")
        , ("updateDownloadingOperation", "Downloading update {0}", "Baixando atualização {0}", "Descargando actualización {0}", "正在下载更新 {0}")
        , ("updateReadyOperation", "Update {0} ready; it will be installed when closing", "Atualização {0} pronta; será instalada ao fechar", "Actualización {0} lista; se instalará al cerrar", "更新 {0} 已就绪；关闭时将安装")
        , ("updateDownloadCancelled", "Update download canceled", "Download da atualização cancelado", "Descarga de actualización cancelada", "更新下载已取消")
        , ("updateCancelled", "Download canceled.", "Download cancelado.", "Descarga cancelada.", "下载已取消。")
        , ("updateNotDownloaded", "Update not downloaded: {0}", "Atualização não baixada: {0}", "Actualización no descargada: {0}", "更新未下载：{0}")
        , ("updateDownloadFailed", "Could not download version {0}: {1}", "Não foi possível baixar a versão {0}: {1}", "No se pudo descargar la versión {0}: {1}", "无法下载版本 {0}：{1}")
        , ("updateInstallFailed", "Update {0} was not installed at the last close: {1} A new attempt will occur when closing.", "A atualização {0} não foi instalada no último fechamento: {1} Uma nova tentativa ocorre ao fechar.", "La actualización {0} no se instaló en el último cierre: {1} Se intentará de nuevo al cerrar.", "上次关闭时未安装更新 {0}：{1} 关闭时将再次尝试。")
        , ("updateReady", "Update {0} ready. It will be installed when closing " + Branding.ProductName + "; click to restart now.", "Atualização {0} pronta. Será instalada ao fechar o " + Branding.ProductName + "; clique para reiniciar agora.", "Actualización {0} lista. Se instalará al cerrar " + Branding.ProductName + "; haga clic para reiniciar ahora.", "更新 {0} 已就绪。关闭 " + Branding.ProductName + " 时将安装；点击立即重新启动。")
        , ("updateRetry", "{0} Version {1} available; click to try again.", "{0} Versão {1} disponível; clique para tentar novamente.", "{0} Versión {1} disponible; haga clic para volver a intentarlo.", "{0} 版本 {1} 可用；点击重试。")
        , ("documentLabel", "Document {0}", "Documento {0}", "Documento {0}", "文档 {0}")
        , ("missingId", "No _id", "Sem _id", "Sin _id", "无 _id")
        , ("invalidJson", "Invalid JSON", "JSON inválido", "JSON no válido", "JSON 无效")
        , ("partialProjection", "partial projection", "projeção parcial", "proyección parcial", "部分投影")
        , ("derivedAggregation", "aggregation", "agregação", "agregación", "聚合")
        , ("invalidJsonDetails", "Invalid JSON: {0}", "JSON inválido: {0}", "JSON no válido: {0}", "JSON 无效：{0}")
        , ("nextFields", "More fields…", "Próximos campos…", "Más campos…", "更多字段…")
        , ("expandMoreFields", "Expand to load 256 more fields; JSON and export preserve the complete content.", "Expandir para carregar mais 256 campos; JSON e exportação conservam o conteúdo completo.", "Expanda para cargar 256 campos más; JSON y exportación conservan el contenido completo.", "展开以加载另外 256 个字段；JSON 和导出保留完整内容。")
        , ("originalContent", "Original content", "Conteúdo original", "Contenido original", "原始内容")
        , ("loadingEllipsis", "Loading…", "Carregando…", "Cargando…", "正在加载…")
        , ("openScriptPicker", "Open script", "Abrir script", "Abrir script", "打开脚本")
        , ("javascriptOrJson", "JavaScript or JSON", "JavaScript ou JSON", "JavaScript o JSON", "JavaScript 或 JSON")
        , ("openFailed", "Could not open", "Não foi possível abrir", "No se pudo abrir", "无法打开")
        , ("saveScriptPicker", "Save script", "Salvar script", "Guardar script", "保存脚本")
        , ("scriptOrQuery", "Script / query", "Script / consulta", "Script / consulta", "脚本 / 查询")
        , ("notSaved", "File not saved", "Arquivo não salvo", "Archivo no guardado", "文件未保存")
        , ("executionInProgress", "Execution in progress", "Execução em andamento", "Ejecución en curso", "正在执行")
        , ("stopExecutionPrompt", "Interrupt this operation and wait? Effects already sent to the server are not reverted.", "Interromper esta operação e aguardar? Efeitos já enviados ao servidor não são revertidos.", "¿Interrumpir esta operación y esperar? Los efectos ya enviados al servidor no se revierten.", "中断此操作并等待？已发送到服务器的效果不会回滚。")
        , ("interrupt", "Interrupt", "Interromper", "Interrumpir", "中断")
        , ("tabClosePrompt", "Close tab", "Fechar aba", "Cerrar pestaña", "关闭标签页")
        , ("saveChangesPrompt", "Save changes to {0}?", "Salvar as alterações de {0}?", "¿Guardar los cambios de {0}?", "保存对 {0} 的更改？")
        , ("discard", "Discard", "Descartar", "Descartar", "放弃")
        , ("openConnectionTarget", "Open the connection and choose the tab target.", "Abra a conexão e escolha o destino da aba.", "Abra la conexión y elija el destino de la pestaña.", "打开连接并选择标签页目标。")
        , ("cancelBeforeClose", "Cancel the operation and wait before closing.", "Cancele a operação e aguarde antes de fechar.", "Cancele la operación y espere antes de cerrar.", "请取消操作并等待后再关闭。")
        , ("updateReadyPrompt", "Version {0} will be installed when " + Branding.ProductName + " closes. Restart now? Running tabs and drafts follow normal close confirmations.", "A versão {0} será instalada quando o " + Branding.ProductName + " fechar. Reiniciar agora? Abas em execução e rascunhos seguem as confirmações de um fechamento normal.", "La versión {0} se instalará cuando se cierre " + Branding.ProductName + ". ¿Reiniciar ahora? Las pestañas en ejecución y los borradores siguen las confirmaciones normales de cierre.", "" + Branding.ProductName + " 关闭时将安装版本 {0}。现在重新启动？正在运行的标签页和草稿遵循正常关闭确认。")
        , ("restartNow", "Restart now", "Reiniciar agora", "Reiniciar ahora", "立即重新启动")
        , ("later", "Later", "Depois", "Después", "稍后")
        , ("runningTool", "Tool in progress", "Ferramenta em execução", "Herramienta en ejecución", "工具正在运行")
        , ("finishToolBeforeExit", "Complete or cancel the operation in the tools window before exiting.", "Conclua ou cancele a operação na janela de ferramentas antes de sair.", "Complete o cancele la operación en la ventana de herramientas antes de salir.", "退出前请在工具窗口中完成或取消操作。")
        , ("sessionNotSaved", "Session not saved", "Sessão não salva", "Sesión no guardada", "会话未保存")
        , ("closeWithoutRecovery", "Close without recovering", "Fechar sem recuperar", "Cerrar sin recuperar", "不恢复并关闭")
        , ("closeWithoutRecoveryPrompt", "Close without recovering this session's changes?", "Fechar sem recuperar as alterações desta sessão?", "¿Cerrar sin recuperar los cambios de esta sesión?", "关闭且不恢复此会话的更改？")
        , ("connectBeforeTools", "Connect to the source before opening tools.", "Conecte a origem antes de abrir as ferramentas.", "Conéctese al origen antes de abrir las herramientas.", "打开工具前请连接源。")
        , ("toolsTitle", "Tools", "Ferramentas", "Herramientas", "工具")
        , ("removeIndex", "Remove index", "Remover índice", "Eliminar índice", "删除索引")
        , ("removeIndexPrompt", "Remove index {0}?", "Remover o índice {0}?", "¿Eliminar el índice {0}?", "删除索引 {0}？")
        , ("autoSelection", "Automatic selection", "Seleção automática", "Selección automática", "自动选择")
        , ("useInstance", "Use instance", "Usar instância", "Usar instancia", "使用实例")
        , ("noSelectableMember", "No selectable member was provided by the server. The driver keeps automatic selection.", "Nenhum membro selecionável informado pelo servidor. O driver mantém seleção automática.", "El servidor no proporcionó ningún miembro seleccionable. El controlador mantiene la selección automática.", "服务器未提供可选成员。驱动程序保持自动选择。")
        , ("instancesTitle", "Instances", "Instâncias", "Instancias", "实例")
        , ("preferencesTitle", "Preferences", "Preferências", "Preferencias", "偏好设置")
        , ("appearanceDrafts", "Appearance and drafts", "Aparência e rascunhos", "Apariencia y borradores", "外观和草稿")
        , ("editorFontSize", "Editor font size (12–20)", "Tamanho da fonte do editor (12–20)", "Tamaño de fuente del editor (12–20)", "编辑器字体大小（12–20）")
        , ("recoverDrafts", "Recover drafts when reopening", "Recuperar rascunhos ao reabrir", "Recuperar borradores al volver a abrir", "重新打开时恢复草稿")
        , ("recoverConnectionDrafts", "Recover drafts from this connection", "Recuperar rascunhos desta conexão", "Recuperar borradores de esta conexión", "恢复此连接的草稿")
        , ("autocomplete", "Autocomplete…", "Autocomplete…", "Autocompletado…", "自动补全…")
        , ("draftsSensitiveNote", "Tab text is saved locally and may contain sensitive data. Results and connection credentials are not recovered. JSON input is saved only when you enable this option in the tab.", "O texto das abas é salvo localmente e pode conter dados sensíveis. Resultados e credenciais da conexão não são recuperados. A entrada JSON só é salva quando você habilita essa opção na aba.", "El texto de las pestañas se guarda localmente y puede contener datos sensibles. Los resultados y las credenciales de conexión no se recuperan. La entrada JSON solo se guarda cuando activa esta opción en la pestaña.", "标签页文本保存在本地，可能包含敏感数据。不会恢复结果和连接凭据。只有在标签页中启用此选项时才会保存 JSON 输入。")
        , ("finish", "Finish", "Concluir", "Finalizar", "完成")
        , ("preferencesNotSaved", "Preferences not saved", "Preferências não salvas", "Preferencias no guardadas", "偏好设置未保存")
        , ("clipboardUnavailable", "Clipboard unavailable.", "Área de transferência indisponível.", "Portapapeles no disponible.", "剪贴板不可用。")
        , ("jsonCopied", "Formatted JSON copied to the clipboard.", "JSON formatado copiado para a área de transferência.", "JSON formateado copiado al portapapeles.", "格式化 JSON 已复制到剪贴板。")
        , ("copyFailed", "Could not copy: {0}", "Não foi possível copiar: {0}", "No se pudo copiar: {0}", "无法复制：{0}")
        , ("confirm", "Confirm", "Confirmar", "Confirmar", "确认")
        , ("confirmMutation", "Confirm this operation?", "Confirmar esta operação?", "¿Confirmar esta operación?", "确认此操作？")
        , ("sessionRestoreFailed", "Could not recover the session: {0}", "Não foi possível recuperar a sessão: {0}", "No se pudo recuperar la sesión: {0}", "无法恢复会话：{0}")
        , ("draftsRecovered", "Drafts recovered; connections remain closed.", "Rascunhos recuperados; conexões permanecem fechadas.", "Borradores recuperados; las conexiones permanecen cerradas.", "草稿已恢复；连接仍保持关闭。")
        , ("draftsUpdated", "Local drafts updated", "Rascunhos locais atualizados", "Borradores locales actualizados", "本地草稿已更新")
        , ("draftRecoveryDisabled", "Draft recovery disabled", "Recuperação de rascunhos desativada", "Recuperación de borradores desactivada", "草稿恢复已禁用")
        , ("draftNotSaved", "Draft not saved: {0}", "Rascunho não salvo: {0}", "Borrador no guardado: {0}", "草稿未保存：{0}")
        , ("sessionNotLoaded", "The previous session was not loaded. It will be preserved; save your scripts to files.", "A sessão anterior não foi carregada. Ela será preservada; salve seus scripts em arquivos.", "La sesión anterior no se cargó. Se conservará; guarde sus scripts en archivos.", "上一个会话未加载。它将被保留；请将脚本保存到文件。")
        , ("suggestionsUnavailableShortcut", "Suggestion unavailable; use {0} for Console/MQL suggestions.", "Sugestão não disponível; use {0} para sugestões de Console/MQL.", "Sugerencia no disponible; use {0} para sugerencias de Console/MQL.", "建议不可用；使用 {0} 获取 Console/MQL 建议。")
        , ("suggestionsUnavailable", "Suggestion unavailable; open the suggestion list with the configured shortcut.", "Sugestão não disponível; abra a lista de sugestões pelo atalho configurado.", "Sugerencia no disponible; abra la lista con el atajo configurado.", "建议不可用；使用配置的快捷键打开建议列表。")
        , ("traditionalSuggestionsError", "Could not load suggestions right now.", "Não foi possível carregar sugestões agora.", "No se pudieron cargar las sugerencias ahora.", "暂时无法加载建议。")
        , ("traditionalLoading", "Loading suggestions…", "Carregando sugestões…", "Cargando sugerencias…", "正在加载建议…")
        , ("noSuggestionsMatch", "No suggestion matches the current text", "Nenhuma sugestão corresponde ao texto atual", "Ninguna sugerencia coincide con el texto actual", "没有建议匹配当前文本")
        , ("shortcutUnconfigured", "shortcut not configured", "atalho não configurado", "atajo no configurado", "快捷键未配置")
        , ("moveVerb", "move", "mover", "mover", "移动")
        , ("acceptVerb", "accept", "aceita", "acepta", "接受")
        , ("closeVerb", "close", "fecha", "cierra", "关闭")
        , ("traditionalUnavailable", "Traditional suggestions are unavailable in this tab.", "Sugestões tradicionais indisponíveis nesta aba.", "Las sugerencias tradicionales no están disponibles en esta pestaña.", "此标签页无法使用传统建议。")
        , ("noStatementAtCursor", "// No statement at cursor.", "// Nenhum statement no cursor.", "// No hay statement en el cursor.", "// 光标处没有语句。")
        , ("confirmConsoleWrite", "Confirm Console write", "Confirmar escrita no Console", "Confirmar escritura en Console", "确认 Console 写入")
        , ("confirmSendOperation", "Confirm sending this operation?", "Confirmar o envio desta operação?", "¿Confirmar el envío de esta operación?", "确认发送此操作？")
        , ("execute", "Execute", "Executar", "Ejecutar", "执行")
        , ("validSyntax", "Syntax valid locally", "Sintaxe válida localmente", "Sintaxis válida localmente", "语法在本地有效")
        , ("errorsAvailable", "Diagnostics available in Errors", "Diagnóstico disponível em Erros", "Diagnóstico disponible en Errores", "错误中有可用诊断")
        , ("validationCancelled", "Validation canceled", "Validação cancelada", "Validación cancelada", "验证已取消")
        , ("validationIncomplete", "Validation not completed: {0}", "Validação não concluída: {0}", "Validación no completada: {0}", "验证未完成：{0}")
        , ("formatChangedDiscarded", "Text changed during formatting; result discarded.", "Texto alterado durante a formatação; resultado descartado.", "El texto cambió durante el formateo; resultado descartado.", "格式化期间文本发生更改；结果已丢弃。")
        , ("formatCompleted", "Formatting completed — Ctrl+Z to undo", "Formatação concluída — Ctrl+Z para desfazer", "Formateo completado — Ctrl+Z para deshacer", "格式化完成 — Ctrl+Z 撤销")
        , ("formatCancelled", "Formatting canceled", "Formatação cancelada", "Formateo cancelado", "格式化已取消")
        , ("formatFailed", "Could not format: {0}", "Não foi possível formatar: {0}", "No se pudo formatear: {0}", "无法格式化：{0}")
        , ("formatIncomplete", "Formatting not completed; see Errors.", "Formatação não concluída; consulte Erros.", "Formateo no completado; consulte Errores.", "格式化未完成；请查看错误。")
        , ("exportLoadedPage", "Export loaded page — JSON or CSV", "Exportar página carregada — JSON ou CSV", "Exportar página cargada — JSON o CSV", "导出已加载页面 — JSON 或 CSV")
        , ("pageExported", "Page exported — {0} document(s).", "Página exportada — {0} documentos.", "Página exportada — {0} documentos.", "页面已导出 — {0} 个文档。")
        , ("exportCancelled", "Export canceled.", "Exportação cancelada.", "Exportación cancelada.", "导出已取消。")
        , ("exportIncomplete", "Export not completed; check destination and write permission.", "Exportação não concluída; verifique destino e permissão de escrita.", "Exportación no completada; compruebe el destino y los permisos de escritura.", "导出未完成；请检查目标和写入权限。")
        , ("exportFailed", "Export not completed", "Exportação não concluída", "Exportación no completada", "导出未完成")
        , ("databaseLabel", "Database", "Banco", "Base de datos", "数据库")
        , ("legacyAggregationCollection", "Collection (legacy aggregation)", "Coleção (agregação legada)", "Colección (agregación heredada)", "集合（旧版聚合）")
        , ("chooseOpenConnectionDatabase", "Choose an open connection and a database.", "Escolha uma conexão aberta e um banco.", "Elija una conexión abierta y una base de datos.", "选择打开的连接和数据库。")
        , ("applyTarget", "Apply target", "Aplicar destino", "Aplicar destino", "应用目标")
        , ("targetConnectionOpen", "Open connection", "Conexão aberta", "Conexión abierta", "打开连接")
        , ("viewDocumentJson", "View document as JSON", "Visualizar documento em JSON", "Ver documento como JSON", "以 JSON 查看文档")
        , ("editDocument", "Open document for editing", "Abrir documento para edição", "Abrir documento para editar", "打开文档进行编辑")
        , ("copyId", "Copy _id", "Copiar _id", "Copiar _id", "复制 _id")
        , ("copyIdQuery", "Copy query by _id", "Copiar consulta por _id", "Copiar consulta por _id", "复制按 _id 查询")
        , ("copyUuidId", "Copy equivalent UUID of _id", "Copiar UUID equivalente do _id", "Copiar UUID equivalente de _id", "复制 _id 的等效 UUID")
        , ("noDocumentCursor", "No document at cursor. Place the cursor inside a document.", "Nenhum documento no cursor. Posicione o cursor dentro de um documento.", "No hay documento en el cursor. Coloque el cursor dentro de un documento.", "光标处没有文档。请将光标放在文档内。")
        , ("copySelectedText", "Copy selected text", "Copiar texto selecionado", "Copiar texto seleccionado", "复制选中文本")
        , ("openDocumentFailed", "Could not open document: {0}", "Não foi possível abrir o documento: {0}", "No se pudo abrir el documento: {0}", "无法打开文档：{0}")
        , ("aiDisabled", "AI is disabled in preferences.", "IA desabilitada nas preferências.", "La IA está deshabilitada en las preferencias.", "偏好设置中已禁用 AI。")
        , ("aiNotConfigured", "Explicit AI is unavailable in this tab.", "IA explícita não está disponível nesta aba.", "La IA explícita no está disponible en esta pestaña.", "此标签页中显式 AI 不可用。")
        , ("aiFailed", "Local AI could not handle this request.", "A IA local não pôde atender a este pedido.", "La IA local no pudo atender esta solicitud.", "本地 AI 无法处理此请求。")
        , ("traditionalDisabled", "Traditional list disabled in preferences.", "Lista tradicional desligada nas preferências.", "Lista tradicional deshabilitada en las preferencias.", "偏好设置中已禁用传统列表。")
        , ("aiPrivacyFailure", "Context contains a possible secret; local AI was not queried.", "Contexto contém possível segredo; a IA local não foi consultada.", "El contexto contiene un posible secreto; no se consultó la IA local.", "上下文可能包含机密；未查询本地 AI。")
        , ("aiBudgetFailure", "Minimum context does not fit the selected model window.", "O contexto mínimo não cabe na janela do modelo selecionado.", "El contexto mínimo no cabe en la ventana del modelo seleccionado.", "最小上下文不适合所选模型窗口。")
        , ("aiRejectedFailure", "Local AI did not produce a usable suggestion.", "A IA local não produziu uma sugestão utilizável.", "La IA local no produjo una sugerencia utilizable.", "本地 AI 未生成可用建议。")
        , ("aiTimeoutFailure", "Local AI exceeded the timeout without producing a suggestion.", "A IA local excedeu o tempo limite sem produzir uma sugestão.", "La IA local superó el tiempo de espera sin producir una sugerencia.", "本地 AI 超时且未生成建议。")
        , ("aiPreemptedFailure", "Generation discarded by a higher-priority action.", "Geração descartada por uma ação de prioridade maior.", "Generación descartada por una acción de mayor prioridad.", "生成已被更高优先级操作丢弃。")
        , ("aiRetry", " Try again in {0} s.", " Nova tentativa em {0} s.", " Inténtelo de nuevo en {0} s.", " 请在 {0} 秒后重试。")
        , ("noAiModelSelected", "No AI model selected; choose one in Preferences.", "Nenhum modelo de IA selecionado; escolha um em Preferências.", "No hay ningún modelo de IA seleccionado; elija uno en Preferencias.", "未选择 AI 模型；请在偏好设置中选择。")
        , ("aiModelInvalid", "The selected model package cannot be used by this version.", "O pacote de modelo selecionado não pode ser usado por esta versão.", "Esta versión no puede usar el paquete de modelo seleccionado.", "此版本无法使用所选模型包。")
        , ("aiCapabilityMissing", "The selected model does not declare autocomplete capability.", "O modelo selecionado não declara a capacidade de autocomplete.", "El modelo seleccionado no declara capacidad de autocompletado.", "所选模型未声明自动补全能力。")
        , ("aiProviderUnavailable", "The accelerator required by the model is unavailable on this machine.", "O acelerador exigido pelo modelo não está disponível nesta máquina.", "El acelerador requerido por el modelo no está disponible en esta máquina.", "此计算机上无法使用模型所需的加速器。")
        , ("aiCooldown", "Local AI is unavailable after a recent failure.", "IA local indisponível após uma falha recente.", "La IA local no está disponible tras un fallo reciente.", "本地 AI 在最近一次失败后不可用。")
        , ("aiNotLoaded", "No AI model loaded.", "Nenhum modelo de IA carregado.", "No hay ningún modelo de IA cargado.", "未加载 AI 模型。")
        , ("aiRuntimeFailure", "Local AI runtime failure; check model status.", "Falha do runtime de IA local; confira o status do modelo.", "Fallo del runtime de IA local; compruebe el estado del modelo.", "本地 AI 运行时失败；请检查模型状态。")
        , ("identifierSnippetInitial", "Generate an identifier using this connection's UUID mode and representation.", "Gere um identificador no modo e na representação UUID desta conexão.", "Genere un identificador con el modo y la representación UUID de esta conexión.", "使用此连接的 UUID 模式和表示形式生成标识符。")
        , ("equivalentUuidNote", "Equivalent UUID: {0} (alternative representation; ObjectId is unchanged)", "UUID equivalente: {0} (representação alternativa; o ObjectId não é alterado)", "UUID equivalente: {0} (representación alternativa; el ObjectId no cambia)", "等效 UUID：{0}（替代表示；ObjectId 不变）")
        , ("noVisibleItems", "No visible items.", "Nenhum item visível.", "No hay elementos visibles.", "没有可见项目。")
        , ("documentsNode", "Documents", "Documentos", "Documentos", "文档")
        , ("indexesNode", "Indexes", "Índices", "Índices", "索引")
        , ("noFolderShort", "No folder", "Sem pasta", "Sin carpeta", "无文件夹")
        , ("noEnvironmentShort", "No environment", "Sem ambiente", "Sin entorno", "无环境")
        , ("detailsTitle", "Details", "Detalhes", "Detalles", "详细信息")
        , ("selectTreeItem", "Select an item in the tree.", "Selecione um item da árvore.", "Seleccione un elemento del árbol.", "选择树中的项目。")
        , ("indexDetailText", "Name: {0}\nFields / direction:\n{1}\nUnique: {2}\nSparse: {3}\nTTL: {4}\nPartial filter: {5}\n\nComplete definition:\n{6}", "Nome: {0}\nCampos / direção:\n{1}\nUnique: {2}\nSparse: {3}\nTTL: {4}\nFiltro parcial: {5}\n\nDefinição completa:\n{6}", "Nombre: {0}\nCampos / dirección:\n{1}\nUnique: {2}\nSparse: {3}\nTTL: {4}\nFiltro parcial: {5}\n\nDefinición completa:\n{6}", "名称：{0}\n字段/方向：\n{1}\n唯一：{2}\n稀疏：{3}\nTTL：{4}\n部分过滤器：{5}\n\n完整定义：\n{6}")
        , ("yes", "Yes", "Sim", "Sí", "是")
        , ("no", "No", "Não", "No", "否")
        , ("disconnectedMetadata", "Disconnected. Connect to load metadata.", "Desconectada. Conecte para carregar metadados.", "Desconectada. Conéctese para cargar metadatos.", "已断开。连接后加载元数据。")
        , ("serverDetails", "Server(s): {0}\nDefault database: {1}\nRead-only: {2}\n{3} · {4} · {5}\n{6}", "Servidor(es): {0}\nBanco padrão: {1}\nSomente leitura: {2}\n{3} · {4} · {5}\n{6}", "Servidor(es): {0}\nBase de datos predeterminada: {1}\nSolo lectura: {2}\n{3} · {4} · {5}\n{6}", "服务器：{0}\n默认数据库：{1}\n只读：{2}\n{3} · {4} · {5}\n{6}")
        , ("noReplicaSet", "no replica set", "sem replica set", "sin replica set", "无副本集")
        , ("loadDetailsFailed", "Could not load details: {0}", "Não foi possível carregar os detalhes: {0}", "No se pudieron cargar los detalles: {0}", "无法加载详细信息：{0}")
        , ("identifierModeTitle", "Default identifier representation", "Representação padrão de identificadores", "Representación predeterminada de identificadores", "默认标识符表示形式")
        , ("objectIdBytesNote", "12 ObjectId bytes + 4 zero bytes · alternative representation: not UUID v4 and does not change the stored ObjectId", "12 bytes do ObjectId + 4 bytes zero · representação alternativa: não é UUID v4 e não altera o ObjectId gravado", "12 bytes de ObjectId + 4 bytes cero · representación alternativa: no es UUID v4 y no cambia el ObjectId almacenado", "12 个 ObjectId 字节 + 4 个零字节 · 替代表示：不是 UUID v4，不会更改存储的 ObjectId")
        , ("identifierSaved", "Identifier mode saved. Open results were updated; no stored data changed.", "Modo de identificador salvo. Resultados abertos foram atualizados; nenhum dado gravado foi alterado.", "Modo de identificador guardado. Se actualizaron los resultados abiertos; no cambiaron datos almacenados.", "标识符模式已保存。已更新打开的结果；未更改存储数据。")
        , ("identifierSessionOnly", "Mode applied in this session but not saved: {0}", "Modo aplicado nesta sessão, mas não salvo: {0}", "Modo aplicado en esta sesión, pero no guardado: {0}", "模式已应用于本会话但未保存：{0}")
        , ("noDocumentsNotice", "No documents", "Sem documentos", "Sin documentos", "无文档")
        , ("noDocumentInResultNotice", "No document in this result.", "Nenhum documento neste resultado.", "No hay ningún documento en este resultado.", "此结果中没有文档。")
        , ("partialProjectionNotice", "Partial projection", "Projeção parcial", "Proyección parcial", "部分投影")
        , ("partialProjectionExplain", "Fields omitted by the projection do not appear; editing is unavailable.", "Campos omitidos pela projeção não aparecem; a edição fica indisponível.", "Los campos omitidos por la proyección no aparecen; la edición no está disponible.", "投影省略的字段不会显示；编辑不可用。")
        , ("derivedNotice", "Aggregation", "Agregação", "Agregación", "聚合")
        , ("derivedExplain", "Documents transformed by the pipeline may not correspond to stored documents.", "Documentos transformados pelo pipeline podem não corresponder aos armazenados.", "Los documentos transformados por el pipeline pueden no corresponder a los almacenados.", "管道转换的文档可能不对应存储的文档。")
        , ("limitedNotice", "Limited result", "Resultado limitado", "Resultado limitado", "结果已限制")
        , ("limitedExplain", "The server may have more documents; adjust filter, skip or limit.", "O servidor pode ter mais documentos; ajuste filtro, skip ou limit.", "El servidor puede tener más documentos; ajuste filtro, skip o limit.", "服务器可能有更多文档；请调整过滤器、skip 或 limit。")
        , ("hardwareProfileSummary", "Selected profile: {0} {1} · {2:0.#} GiB · {3}", "Perfil selecionado: {0} {1} · {2:0.#} GiB · {3}", "Perfil seleccionado: {0} {1} · {2:0.#} GiB · {3}", "已选择配置：{0} {1} · {2:0.#} GiB · {3}")
        , ("hardwareDetectionWaiting", "Waiting for hardware detection. Manual profiles are for planning only.", "Aguardando detecção do hardware. Perfis manuais servem somente para planejamento.", "Esperando la detección del hardware. Los perfiles manuales solo sirven para planificación.", "正在等待硬件检测。手动配置仅用于规划。")
        , ("hardwareProfileRequired", "Enter a GPU name and memory in GiB to create the profile.", "Informe nome e memória da GPU em GiB para criar o perfil.", "Indica el nombre y la memoria de la GPU en GiB para crear el perfil.", "请输入 GPU 名称和 GiB 内存以创建配置。")
        , ("hardwareProfileRemoved", "Manual profile removed. Detected hardware is used again.", "Perfil manual removido. O hardware detectado voltou a ser usado.", "Perfil manual eliminado. Se volverá a usar el hardware detectado.", "已删除手动配置。将再次使用检测到的硬件。")
        , ("contextTokensDigitsOnly", "Context tokens must contain digits only, without periods or commas.", "Tokens de contexto devem conter somente dígitos, sem ponto ou vírgula.", "Los tokens de contexto solo deben contener dígitos, sin puntos ni comas.", "上下文令牌只能包含数字，不能有点或逗号。")
        , ("completionTokensDigitsOnly", "Generated tokens must contain digits only, without periods or commas.", "Tokens gerados devem conter somente dígitos, sem ponto ou vírgula.", "Los tokens generados solo deben contener dígitos, sin puntos ni comas.", "生成令牌只能包含数字，不能有点或逗号。")
        , ("contextTokensRange", "Context tokens must be between 64 and {0}.", "Tokens de contexto devem ficar entre 64 e {0}.", "Los tokens de contexto deben estar entre 64 y {0}.", "上下文令牌必须介于 64 和 {0} 之间。")
        , ("completionTokensRange", "Generated tokens must be between 1 and {0}.", "Tokens gerados devem ficar entre 1 e {0}.", "Los tokens generados deben estar entre 1 y {0}.", "生成令牌必须介于 1 和 {0} 之间。")
        , ("tokenWindowExceeded", "Context + output + overhead exceed the model window ({0} tokens).", "Contexto + saída + overhead excedem a janela do modelo ({0} tokens).", "El contexto + la salida + la sobrecarga superan la ventana del modelo ({0} tokens).", "上下文 + 输出 + 开销超过模型窗口（{0} 个令牌）。")
        , ("conservativeTokenWindow", "The model window was not declared; the conservative limit of {0} tokens will be applied.", "A janela do modelo não foi declarada; o teto conservador de {0} tokens será aplicado.", "La ventana del modelo no está declarada; se aplicará el límite conservador de {0} tokens.", "模型窗口未声明；将应用 {0} 个令牌的保守上限。")
        , ("remoteModelDetails", "License {0} · {1}", "Licença {0} · {1}", "Licencia {0} · {1}", "许可证 {0} · {1}")
        , ("licenseNotProvided", "not provided", "não informada", "no proporcionada", "未提供")
        , ("alreadyInstalledAt", "already installed at ", "já instalado em ", "ya instalado en ", "已安装于 ")
        , ("installsAt", "installs at ", "instala em ", "se instalará en ", "安装到 ")
        , ("connectionUuidTitle", "UUID representation for this connection", "Representação UUID desta conexão", "Representación UUID de esta conexión", "此连接的 UUID 表示形式")
        , ("notFound", "not found", "não encontrado", "no encontrado", "未找到")
        , ("external", "external", "externo", "externo", "外部")
        , ("modelFolder", "folder {0}", "pasta {0}", "carpeta {0}", "文件夹 {0}")
        , ("gpuNotDetected", "GPU not detected on this machine", "GPU não detectada nesta máquina", "GPU no detectada en esta máquina", "此计算机未检测到 GPU")
        , ("installed", "installed", "instalado", "instalado", "已安装")
        , ("truncatedCharacters", "… ({0} characters)", "… ({0} caracteres)", "… ({0} caracteres)", "…（{0} 个字符）")
        , ("resultDefault", "result", "resultado", "resultado", "结果")
        , ("documentCount", "{0} document(s)", "{0} documento(s)", "{0} documento(s)", "{0} 个文档")
        , ("documentTypeLabel", "Document", "Documento", "Documento", "文档")
        , ("limitedFlag", "limited", "limitado", "limitado", "已限制")
        , ("partialProjectionFlag", "partial projection", "projeção parcial", "proyección parcial", "部分投影")
        , ("derivedFlag", "aggregation", "agregação", "agregación", "聚合")
        , ("editNeedsConnectedCollection", "Open a connected collection and wait for the query.", "Abra uma coleção conectada e aguarde a consulta.", "Abra una colección conectada y espere la consulta.", "打开已连接的集合并等待查询完成。")
        , ("editResultNeedsCollection", "Select a collection result before editing documents.", "Selecione um resultado de coleção antes de editar documentos.", "Seleccione un resultado de colección antes de editar documentos.", "编辑文档前请选择集合结果。")
        , ("editDocumentNeedsId", "Select a document with _id; do not exclude this field from the projection.", "Selecione um documento com _id; não exclua esse campo da projeção.", "Seleccione un documento con _id; no excluya este campo de la proyección.", "请选择包含 _id 的文档；不要在投影中排除该字段。")
        , ("editDocumentUnavailable", "The document is no longer available. Refresh the page.", "O documento não está mais disponível. Atualize a página.", "El documento ya no está disponible. Actualice la página.", "文档已不可用。请刷新页面。")
        , ("scriptResultsPlaceholder", "The structured result and mongosh console appear here.", "O resultado estruturado e o console do mongosh aparecem aqui.", "El resultado estructurado y la consola de mongosh aparecen aquí.", "结构化结果和 mongosh 控制台会显示在这里。")
        , ("connected", "connected", "conectada", "conectada", "已连接")
        , ("disconnected", "disconnected", "desconectada", "desconectada", "已断开")
        , ("expandToLoad", "Expand to load", "Expandir para carregar", "Expandir para cargar", "展开以加载")
        , ("connectBeforeExpand", "Connect the source before expanding.", "Conecte a origem antes de expandir.", "Conecte el origen antes de expandir.", "展开前请连接来源。")
        , ("hardwareProfileAdded", "Profile {0} {1} added for estimates.", "Perfil {0} {1} adicionado para estimativas.", "Perfil {0} {1} añadido para estimaciones.", "已添加配置 {0} {1}，用于估算。")
        , ("queryingOnnx", "Querying ONNX Runtime…", "Consultando ONNX Runtime…", "Consultando ONNX Runtime…", "正在查询 ONNX Runtime…")
        , ("unavailable", "unavailable", "indisponível", "no disponible", "不可用")
        , ("modelVersion", "version {0}", "versão {0}", "versión {0}", "版本 {0}")
        , ("modelCapabilities", "capabilities: {0}", "capacidades: {0}", "capacidades: {0}", "能力：{0}")
        , ("modelDomain", "domain: {0}", "domínio: {0}", "dominio: {0}", "领域：{0}")
        , ("declaredHardware", "declared hardware: {0}", "hardware declarado: {0}", "hardware declarado: {0}", "声明的硬件：{0}")
        , ("none", "none", "nenhuma", "ninguna", "无")
        , ("traditionalItemsStatus", "{0} item(s){1} · {2}", "{0} itens{1} · {2}", "{0} elementos{1} · {2}", "{0} 个项目{1} · {2}")
        , ("traditionalDataLoading", " · data still loading", " · dados ainda carregando", " · datos aún cargando", " · 数据仍在加载")
        , ("loadingDetails", "Loading details…", "Carregando detalhes…", "Cargando detalles…", "正在加载详细信息…")
        , ("aiCompletionStarting", "Generating suggestion with local AI…", "Gerando sugestão com a IA local…", "Generando sugerencia con la IA local…", "正在使用本地 AI 生成建议…")
        , ("aiCompletionStreaming", "Generating suggestion with local AI… (Esc cancels)", "Gerando sugestão com a IA local… (Esc cancela)", "Generando sugerencia con la IA local… (Esc cancela)", "正在使用本地 AI 生成建议…（Esc 取消）")
        , ("aiCompletionLoading", "Loading model… (Esc cancels waiting)", "Carregando modelo… (Esc cancela a espera)", "Cargando modelo… (Esc cancela la espera)", "正在加载模型…（Esc 取消等待）")
        , ("documentMutationTitle", "{0} document", "{0} documento", "{0} documento", "{0}文档")
        , ("editDocumentTitle", "Edit document · {0}", "Editar documento · {0}", "Editar documento · {0}", "编辑文档 · {0}")
        , ("errorOperationCancelled", "Operation canceled", "Operação cancelada", "Operación cancelada", "操作已取消")
        , ("errorTimeout", "Timed out", "Tempo limite excedido", "Tiempo agotado", "已超时")
        , ("errorInvalidJson", "Invalid JSON", "JSON inválido", "JSON no válido", "JSON 无效")
        , ("errorAuthentication", "Authentication failed", "Falha de autenticação", "Falló la autenticación", "身份验证失败")
        , ("errorConnection", "Connection failed", "Falha de conexão", "Falló la conexión", "连接失败")
        , ("errorMongoQuery", "MongoDB query error", "Erro de consulta MongoDB", "Error de consulta de MongoDB", "MongoDB 查询错误")
        , ("errorMongo", "MongoDB error", "Erro MongoDB", "Error de MongoDB", "MongoDB 错误")
        , ("errorExport", "Export error", "Erro de exportação", "Error de exportación", "导出错误")
        , ("errorFileStorage", "File or storage error", "Erro de arquivo ou armazenamento", "Error de archivo o almacenamiento", "文件或存储错误")
        , ("errorInvalidInput", "Invalid input", "Entrada inválida", "Entrada no válida", "输入无效")
        , ("errorOperationIncomplete", "Operation not completed", "Operação não concluída", "Operación no completada", "操作未完成")
        , ("errorMongoDetailsSuffix", ". Check the target, permissions and parameters provided.", ". Verifique o destino, as permissões e os parâmetros informados.", ". Compruebe el destino, los permisos y los parámetros indicados.", "。请检查目标、权限和提供的参数。")
        , ("validatingSyntaxOperation", "Validating local syntax", "Validando sintaxe local", "Validando sintaxis local", "正在验证本地语法")
        , ("textContextChangedDiscarded", "Text or context changed; diagnostics discarded.", "Texto ou contexto alterado; diagnóstico descartado.", "El texto o el contexto cambió; se descartó el diagnóstico.", "文本或上下文已更改；已丢弃诊断结果。")
        , ("editorPrefix", "Editor: ", "Editor: ", "Editor: ", "编辑器：")
        , ("selectionPrefix", "Selection: ", "Seleção: ", "Selección: ", "选区：")
        , ("formattingQuery", "Formatting query/script", "Formatando query/script", "Formateando consulta/script", "正在格式化查询/脚本")
        , ("exportingPage", "Exporting page — {0} document(s)", "Exportando página — {0} documentos", "Exportando página — {0} documentos", "正在导出页面 — {0} 个文档")
        , ("exportingPageProgress", "Exporting page — {0:N0} of {1:N0} documents", "Exportando página — {0:N0} de {1:N0} documentos", "Exportando página — {0:N0} de {1:N0} documentos", "正在导出页面 — {0:N0}/{1:N0} 个文档")
        , ("csvExportNote", " CSV: strings and headers with a formula prefix receive an apostrophe; BSON round-trip is not guaranteed.", " CSV: strings e cabeçalhos com prefixo de fórmula recebem apóstrofo; não há round-trip BSON garantido.", " CSV: las cadenas y encabezados con prefijo de fórmula reciben un apóstrofo; no se garantiza el round-trip BSON.", " CSV：带公式前缀的字符串和表头会加撇号；不保证 BSON 往返转换。")
        , ("targetWindowTitle", "Target for this tab", "Destino desta aba", "Destino de esta pestaña", "此标签页的目标")
        , ("openConnectionLabel", "Open connection", "Conexão aberta", "Conexión abierta", "打开的连接")
        , ("editUnavailable", "Editing unavailable: {0}", "Edição indisponível: {0}", "Edición no disponible: {0}", "编辑不可用：{0}")
        , ("documentOperationTitle", "{0} document", "{0} documento", "{0} documento", "{0}文档")
        , ("documentOperationContext", "{0} document — {1}", "{0} documento — {1}", "{0} documento — {1}", "{0}文档 — {1}")
        , ("toolsWindowTitle", "Tools", "Ferramentas", "Herramientas", "工具")
        , ("autocompleteModeAuto", "Automatic (recommended)", "Automático (recomendado)", "Automático (recomendado)", "自动（推荐）")
        , ("autocompleteModeBasic", "Basic", "Básico", "Básico", "基础")
        , ("autocompleteModeLocalAi", "Local AI", "IA local", "IA local", "本地 AI")
        , ("hardwareAuto", "Automatic", "Automático", "Automático", "自动")
        , ("hardwareRuntimeNotLoaded", "ONNX Runtime was not loaded in this process.", "O ONNX Runtime não foi carregado neste processo.", "ONNX Runtime no se cargó en este proceso.", "此进程未加载 ONNX Runtime。")
        , ("hardwareGpuProviderUnavailable", "No GPU provider (DirectML or CUDA) is available in this distribution or on this machine.", "Nenhum provider de GPU (DirectML ou CUDA) está disponível nesta distribuição ou máquina.", "Ningún proveedor de GPU (DirectML o CUDA) está disponible en esta distribución o máquina.", "此发行版或计算机上没有可用的 GPU 提供程序（DirectML 或 CUDA）。")
        , ("hardwareNpuProviderUnavailable", "No NPU provider (QNN, OpenVINO or VitisAI) is available in this distribution or on this machine.", "Nenhum provider de NPU (QNN, OpenVINO ou VitisAI) está disponível nesta distribuição ou máquina.", "Ningún proveedor de NPU (QNN, OpenVINO o VitisAI) está disponible en esta distribución o máquina.", "此发行版或计算机上没有可用的 NPU 提供程序（QNN、OpenVINO 或 VitisAI）。")
        , ("selectedModel", "Selected model", "Modelo selecionado", "Modelo seleccionado", "已选模型")
        , ("modelState", "State", "Estado", "Estado", "状态")
        , ("modelInUse", "Model in use", "Modelo em uso", "Modelo en uso", "正在使用的模型")
        , ("backend", "Backend", "Backend", "Backend", "后端")
        , ("provider", "Provider", "Provider", "Provider", "提供程序")
        , ("device", "Device", "Dispositivo", "Dispositivo", "设备")
        , ("loadTime", "Load time", "Tempo de carregamento", "Tiempo de carga", "加载时间")
        , ("loadingTime", "Loading", "Carregamento", "Carga", "加载")
        , ("processMemory", "Process memory", "Memória do processo", "Memoria del proceso", "进程内存")
        , ("firstToken", "First token", "Primeiro token", "Primer token", "首个令牌")
        , ("generation", "Generation", "Geração", "Generación", "生成")
        , ("detectedProvider", "Detected provider", "Provider detectado", "Proveedor detectado", "检测到的提供程序")
        , ("requestedHardware", "Requested hardware", "Hardware solicitado", "Hardware solicitado", "请求的硬件")
        , ("steps", "Steps", "Etapas", "Pasos", "步骤")
        , ("failed", "failed", "falhou", "falló", "失败")
        , ("preferredAccelerationUnavailable", "preferred acceleration unavailable", "aceleração preferida indisponível", "aceleración preferida no disponible", "首选加速不可用")
        , ("modelStateNotInstalled", "Not installed", "Não instalado", "No instalado", "未安装")
        , ("modelStateNotLoaded", "Not loaded", "Não carregado", "No cargado", "未加载")
        , ("modelStateAvailable", "Files found", "Arquivos encontrados", "Archivos encontrados", "已找到文件")
        , ("modelStateLoading", "Loading", "Carregando", "Cargando", "正在加载")
        , ("modelStateReady", "Loaded", "Carregado", "Cargado", "已加载")
        , ("modelStateInvalid", "Invalid", "Inválido", "No válido", "无效")
        , ("modelStateUnsupported", "Unsupported", "Não suportado", "No compatible", "不支持")
        , ("modelStateMissingFiles", "Missing files", "Arquivos ausentes", "Archivos ausentes", "缺少文件")
        , ("modelStateFailed", "Failed", "Falha", "Falló", "失败")
        , ("modelValidityValid", "valid", "válido", "válido", "有效")
        , ("modelValidityUnsupported", "unsupported", "não suportado", "no compatible", "不支持")
        , ("modelValidityMissingFiles", "missing files", "arquivos ausentes", "archivos ausentes", "缺少文件")
        , ("modelValidityInvalid", "invalid", "inválido", "no válido", "无效")
        , ("aiNoModelLoadedBasic", "No model selected; basic autocomplete is available.", "Nenhum modelo selecionado; autocomplete básico disponível.", "No hay ningún modelo seleccionado; el autocompletado básico está disponible.", "未选择模型；基本自动补全可用。")
        , ("aiModelNotLoadedDemand", "Model is not loaded yet; it will be validated on demand.", "Modelo ainda não carregado; será validado sob demanda.", "El modelo aún no está cargado; se validará bajo demanda.", "模型尚未加载；将按需验证。")
        , ("aiContextOverflow", "The full context exceeds the model window. Reduce the content before requesting AI.", "O contexto completo excede a janela do modelo. Reduza o conteúdo antes de solicitar à IA.", "El contexto completo excede la ventana del modelo. Reduzca el contenido antes de solicitar a la IA.", "完整上下文超出模型窗口。请减少内容后再请求 AI。")
        , ("aiTestNoModel", "No model selected. Choose a model from the list or an external folder.", "Nenhum modelo selecionado. Escolha um modelo da lista ou uma pasta externa.", "No hay ningún modelo seleccionado. Elija un modelo de la lista o una carpeta externa.", "未选择模型。请从列表中选择模型或外部文件夹。")
        , ("aiStepFiles", "Folder and files", "Pasta e arquivos", "Carpeta y archivos", "文件夹和文件")
        , ("aiStepTokenizer", "Tokenizer", "Tokenizer", "Tokenizador", "分词器")
        , ("aiStepSession", "ONNX session and provider", "Sessão ONNX e provider", "Sesión ONNX y proveedor", "ONNX 会话和提供程序")
        , ("aiStepGeneration", "Generation", "Geração", "Generación", "生成")
        , ("aiFilesFound", "genai_config.json, ONNX decoder and tokenizer found.", "genai_config.json, decoder ONNX e tokenizer encontrados.", "Se encontraron genai_config.json, el decodificador ONNX y el tokenizador.", "已找到 genai_config.json、ONNX 解码器和分词器。")
        , ("aiTokenizerLoaded", "Loaded with the model.", "Carregado com o modelo.", "Cargado con el modelo.", "已随模型加载。")
        , ("aiSessionCreated", "Session created.", "Sessão criada.", "Sesión creada.", "会话已创建。")
        , ("aiGenerationFailed", "Generation failed. Check memory, provider and ONNX export.", "A geração falhou. Confira memória, provider e exportação ONNX.", "La generación falló. Compruebe la memoria, el proveedor y la exportación ONNX.", "生成失败。请检查内存、提供程序和 ONNX 导出。")
        , ("aiTokensGenerated", "{0} token(s) generated.", "{0} token(s) gerado(s).", "Se generaron {0} token(s).", "已生成 {0} 个令牌。")
        , ("aiNoValidTokens", "No valid token: the model stopped immediately.", "Nenhum token válido: o modelo parou imediatamente.", "No se generaron tokens válidos: el modelo se detuvo inmediatamente.", "没有有效令牌：模型立即停止。")
        , ("aiTestSucceeded", "Model loaded successfully.", "Modelo carregado com sucesso.", "Modelo cargado correctamente.", "模型已成功加载。")
        , ("aiTestNoValidTokens", "The model loaded but generated no valid tokens.", "O modelo carregou, mas não gerou tokens válidos.", "El modelo se cargó, pero no generó tokens válidos.", "模型已加载，但未生成有效令牌。")
        , ("aiDifferentConfiguration", "The loaded model uses another configuration; automatic suggestions do not switch models.", "O modelo carregado é de outra configuração; a sugestão automática não troca de modelo.", "El modelo cargado usa otra configuración; las sugerencias automáticas no cambian de modelo.", "已加载模型使用其他配置；自动建议不会切换模型。")
        , ("aiAutomaticDoesNotLoad", "No model loaded; automatic suggestions do not load a model while typing.", "Nenhum modelo carregado; a sugestão automática não carrega modelo por digitação.", "No hay ningún modelo cargado; las sugerencias automáticas no cargan modelos al escribir.", "未加载模型；自动建议不会在输入时加载模型。")
        , ("aiNoModelSelectedPreferences", "No model selected. Choose a model in Preferences to use local AI.", "Nenhum modelo selecionado. Escolha um modelo em Preferências para usar a IA local.", "No hay ningún modelo seleccionado. Elija un modelo en Preferencias para usar la IA local.", "未选择模型。请在首选项中选择模型以使用本地 AI。")
        , ("aiValidating", "Validating model {0}…", "Validando modelo {0}…", "Validando el modelo {0}…", "正在验证模型 {0}…")
        , ("aiModelUnavailable", "Model {0} unavailable: {1}", "Modelo {0} indisponível: {1}", "Modelo {0} no disponible: {1}", "模型 {0} 不可用：{1}")
        , ("aiLoading", "Loading {0}…", "Carregando {0}…", "Cargando {0}…", "正在加载 {0}…")
        , ("aiLoaded", "Model loaded — {0}", "Modelo carregado — {0}", "Modelo cargado — {0}", "模型已加载 — {0}")
        , ("aiLoadedHardware", "Model loaded — {0}", "Modelo carregado — {0}", "Modelo cargado — {0}", "模型已加载 — {0}")
        , ("aiLoadFailedHardware", "Failed to load {0} — {1}", "Falha ao carregar {0} — {1}", "No se pudo cargar {0}: {1}", "加载 {0} 失败 — {1}")
        , ("aiLoadCancelledDemand", "Loading canceled; the model will be loaded on demand.", "Carregamento cancelado; o modelo será carregado sob demanda.", "Carga cancelada; el modelo se cargará bajo demanda.", "加载已取消；模型将在需要时加载。")
        , ("aiLoadCancelled", "Loading of {0} canceled", "Carregamento de {0} cancelado", "Carga de {0} cancelada", "已取消加载 {0}")
        , ("aiLoadCancelledException", "Model loading canceled.", "Carregamento do modelo cancelado.", "Carga del modelo cancelada.", "模型加载已取消。")
        , ("aiUnsupportedArchitecture", "Architecture is not supported by this runtime. Basic autocomplete remains active.", "Arquitetura não suportada por este runtime. Autocomplete básico ativo.", "La arquitectura no es compatible con este runtime. El autocompletado básico sigue activo.", "此运行时不支持该架构。基本自动补全仍处于活动状态。")
        , ("aiTokenizerFailure", "Failed to load the tokenizer. Use tokenizer.json and tokenizer_config.json from the same model export.", "Falha ao carregar o tokenizer. Use tokenizer.json e tokenizer_config.json da mesma exportação do modelo.", "No se pudo cargar el tokenizador. Use tokenizer.json y tokenizer_config.json de la misma exportación del modelo.", "无法加载分词器。请使用同一模型导出中的 tokenizer.json 和 tokenizer_config.json。")
        , ("aiInitializationFailure", "Failed to initialize the model. Check ONNX files, memory and provider. Basic autocomplete remains active.", "Falha ao inicializar o modelo. Confira arquivos ONNX, memória e provider. Autocomplete básico ativo.", "No se pudo inicializar el modelo. Compruebe los archivos ONNX, la memoria y el proveedor. El autocompletado básico sigue activo.", "模型初始化失败。请检查 ONNX 文件、内存和提供程序。基本自动补全仍处于活动状态。")
        , ("aiLoadFailed", "Failed to load {0}", "Falha ao carregar {0}", "No se pudo cargar {0}", "加载 {0} 失败")
        , ("aiGenerationFailure", "Failed to initialize or generate. Check the model, tokenizer, memory and provider. Basic autocomplete remains active.", "Falha ao inicializar ou gerar. Confira modelo, tokenizer, memória e provider. Autocomplete básico ativo.", "No se pudo inicializar o generar. Compruebe el modelo, el tokenizador, la memoria y el proveedor. El autocompletado básico sigue activo.", "初始化或生成失败。请检查模型、分词器、内存和提供程序。基本自动补全仍处于活动状态。")
        , ("aiLoadedPlain", "Model loaded.", "Modelo carregado.", "Modelo cargado.", "模型已加载。")
        , ("aiLoadedProvider", "Model loaded — {0} ({1})", "Modelo carregado — {0} ({1})", "Modelo cargado — {0} ({1})", "模型已加载 — {0}（{1}）")
        , ("aiPreferredAccelerationUnavailableSuffix", "; preferred acceleration unavailable.", "; aceleração preferida indisponível.", "; aceleración preferida no disponible.", "；首选加速不可用。")
        , ("aiCpuFallbackSuffix", " (incompatible/unavailable acceleration → CPU)", " (aceleração incompatível/indisponível → CPU)", " (aceleración incompatible/no disponible → CPU)", "（加速不兼容/不可用 → CPU）")
        , ("aiReadyMetrics", "Ready · {0}{1} · {2} token(s) in {3:F0} ms", "Pronto · {0}{1} · {2} token(s) em {3:F0} ms", "Listo · {0}{1} · {2} token(s) en {3:F0} ms", "就绪 · {0}{1} · {2} 个令牌，用时 {3:F0} 毫秒")
        , ("aiCatalogNotInstalled", "Model not installed. Select a model directory or an external ONNX GenAI folder. Basic autocomplete remains active.", "Modelo não instalado. Selecione um modelo do diretório ou uma pasta ONNX GenAI externa. Autocomplete básico ativo.", "Modelo no instalado. Seleccione un directorio de modelos o una carpeta ONNX GenAI externa. El autocompletado básico sigue activo.", "模型未安装。请选择模型目录或外部 ONNX GenAI 文件夹。基本自动补全仍处于活动状态。")
        , ("aiCatalogMissingConfig", "Missing file: genai_config.json. Select an ONNX Runtime GenAI export folder.", "Arquivos ausentes: genai_config.json. Selecione a pasta de uma exportação ONNX Runtime GenAI.", "Falta el archivo genai_config.json. Seleccione la carpeta de una exportación ONNX Runtime GenAI.", "缺少文件：genai_config.json。请选择 ONNX Runtime GenAI 导出文件夹。")
        , ("aiCatalogMissingFiles", "Missing or empty files: {0}.", "Arquivos ausentes ou vazios: {0}.", "Archivos ausentes o vacíos: {0}.", "缺少或为空的文件：{0}。")
        , ("aiCatalogUnsupportedArchitecture", "Architecture \"{0}\" is not supported. Supported: {1}.", "Arquitetura \"{0}\" não suportada. Suportadas: {1}.", "La arquitectura \"{0}\" no es compatible. Compatibles: {1}.", "不支持架构“{0}”。支持的架构：{1}。")
        , ("aiCatalogMetadataInvalid", "{0} is invalid: check field names, types and limits.", "{0} inválido: confira nomes, tipos e limites dos campos.", "{0} no válido: compruebe los nombres, tipos y límites de los campos.", "{0} 无效：请检查字段名称、类型和限制。")
        , ("aiCatalogContextContract", "The model requires context contract \"{0}\", which this " + Branding.ProductName + " version does not support. Supported contracts: {1}.", "Modelo requer contrato de contexto \"{0}\", não suportado por esta versão do " + Branding.ProductName + ". Contratos suportados: {1}.", "El modelo requiere el contrato de contexto \"{0}\", no compatible con esta versión de " + Branding.ProductName + ". Contratos compatibles: {1}.", "模型需要上下文契约“{0}”，此版本的 " + Branding.ProductName + " 不支持。支持的契约：{1}。")
        , ("aiCatalogAvailable", "Files found. Use Test model to validate inference and provider.", "Arquivos encontrados. Use Testar modelo para validar inferência e provider.", "Archivos encontrados. Use Probar modelo para validar la inferencia y el proveedor.", "已找到文件。请使用测试模型验证推理和提供程序。")
        , ("aiCatalogInvalid", "Invalid model: check genai_config.json, ONNX decoder, tokenizer.json and tokenizer_config.json.", "Modelo inválido: confira genai_config.json, decoder ONNX, tokenizer.json e tokenizer_config.json.", "Modelo no válido: compruebe genai_config.json, el decodificador ONNX, tokenizer.json y tokenizer_config.json.", "模型无效：请检查 genai_config.json、ONNX 解码器、tokenizer.json 和 tokenizer_config.json。")
        , ("aiInitializingCpu", "Initializing CPU — {0}…", "Inicializando CPU — {0}…", "Inicializando CPU — {0}…", "正在初始化 CPU — {0}…")
        , ("aiInitializingHardware", "Initializing {0} ({1}) — {2}…", "Inicializando {0} ({1}) — {2}…", "Inicializando {0} ({1}) — {2}…", "正在初始化 {0}（{1}）— {2}…")
        , ("aiGenerationWithoutFinalChunk", "Generation ended without a final chunk.", "Geração encerrada sem pedaço final.", "La generación terminó sin un fragmento final.", "生成结束时没有最终片段。")
        , ("aiModelNotInitialized", "Model is not initialized.", "Modelo não inicializado.", "El modelo no está inicializado.", "模型未初始化。")
        , ("aiTokenizerIncompatible", "Tokenizer is incompatible with this model.", "Tokenizer incompatível com este modelo.", "El tokenizer no es compatible con este modelo.", "Tokenizer 与此模型不兼容。")
        , ("aiTokenizerNotInitialized", "Tokenizer is not initialized.", "Tokenizer não inicializado.", "El tokenizer no está inicializado.", "Tokenizer 未初始化。")
        , ("aiContextWindowInsufficient", "The context window is insufficient.", "Janela de contexto insuficiente.", "La ventana de contexto es insuficiente.", "上下文窗口不足。")
        , ("aiContextExceedsWindow", "The full context exceeds the model window.", "O contexto completo excede a janela do modelo.", "El contexto completo supera la ventana del modelo.", "完整上下文超出模型窗口。")
        , ("aiProviderFailure", "Provider failure {0}: {1}", "Falha do provider {0}: {1}", "Error del proveedor {0}: {1}", "提供程序 {0} 失败：{1}")
        , ("aiNoHardware", "No available hardware can run this model.", "Nenhum hardware disponível pode executar este modelo.", "Ningún hardware disponible puede ejecutar este modelo.", "没有可用硬件可以运行此模型。")
        , ("aiProviderUnavailableHardware", "This model could not run using {0}.", "Não foi possível executar este modelo utilizando {0}.", "No se pudo ejecutar este modelo usando {0}.", "无法使用 {0} 运行此模型。")
        , ("aiReasonPrefix", "Reason:", "Motivo:", "Motivo:", "原因：")
        , ("aiSelectAutomatic", "You can select: Automatic.", "Você pode selecionar: Automático.", "Puede seleccionar: Automático.", "您可以选择：自动。")
        , ("aiSelectAutomaticCpu", "You can select: Automatic or CPU.", "Você pode selecionar: Automático ou CPU.", "Puede seleccionar: Automático o CPU.", "您可以选择：自动或 CPU。")
        , ("metadataSampling", "Sampling schema — {0}", "Amostrando schema — {0}", "Muestreando el esquema — {0}", "正在采样模式 — {0}")
        , ("metadataSamplingCancelled", "Schema sampling canceled", "Amostragem de schema cancelada", "Muestreo del esquema cancelado", "模式采样已取消")
        , ("metadataSamplingUnavailable", "Schema sampling unavailable", "Amostragem de schema indisponível", "Muestreo del esquema no disponible", "模式采样不可用")
        , ("metadataSamplingDiscarded", "Schema sampling discarded — connection or metadata changed", "Amostragem de schema descartada — conexão ou metadados alterados", "Muestreo del esquema descartado: cambió la conexión o los metadatos", "模式采样已丢弃 — 连接或元数据已更改")
        , ("metadataSampled", "Schema sampled — {0} document(s), {1} field(s); names and types only", "Schema amostrado — {0} documento(s), {1} campo(s); somente nomes e tipos", "Esquema muestreado: {0} documento(s), {1} campo(s); solo nombres y tipos", "模式已采样 — {0} 个文档、{1} 个字段；仅包含名称和类型")
        , ("metadataUpdating", "Updating metadata — {0}", "Atualizando metadados — {0}", "Actualizando metadatos — {0}", "正在更新元数据 — {0}")
        , ("metadataUpdated", "Metadata updated — {0}", "Metadados atualizados — {0}", "Metadatos actualizados — {0}", "元数据已更新 — {0}")
        , ("metadataUpdateCancelled", "Metadata update canceled", "Atualização de metadados cancelada", "Actualización de metadatos cancelada", "元数据更新已取消")
        , ("metadataUnavailable", "Metadata unavailable — {0}", "Metadados indisponíveis — {0}", "Metadatos no disponibles — {0}", "元数据不可用 — {0}")
        , ("collectionDefinitionHeader", "Definition and options:", "Definição e opções:", "Definición y opciones:", "定义和选项：")
        , ("topologyLoadBalanced", "Load Balanced", "Balanceado por carga", "Equilibrado", "负载均衡")
        , ("topologyMongosSharded", "Mongos / Sharded", "Mongos / fragmentado", "Mongos / fragmentado", "Mongos / 分片")
        , ("topologyReplicaSet", "Replica Set", "Replica Set", "Conjunto de réplicas", "副本集")
        , ("topologyStandalone", "Standalone", "Standalone", "Independiente", "单机")
        , ("topologyArbiter", "Arbiter", "Árbitro", "Árbitro", "仲裁者")
        , ("topologyPrimary", "Primary", "Primário", "Primario", "主节点")
        , ("topologySecondary", "Secondary / member", "Secundário / membro", "Secundario / miembro", "从节点 / 成员")
        , ("topologyMongos", "Mongos", "Mongos", "Mongos", "Mongos")
        , ("topologyArbiterHint", "Arbiters do not store documents.", "Árbitros não armazenam documentos.", "Los árbitros no almacenan documentos.", "仲裁者不存储文档。")
        , ("topologyDriverRoutingHint", "Automatic driver selection; direct routing is unavailable in this topology.", "Seleção automática do driver; roteamento direto indisponível nesta topologia.", "Selección automática del controlador; el enrutamiento directo no está disponible en esta topología.", "驱动程序自动选择；此拓扑不支持直接路由。")
        , ("topologyDirectHint", "Direct connection; reads use the selected member, writes depend on the server role.", "Conexão direta; leituras no membro escolhido, escritas dependem do papel do servidor.", "Conexión directa; las lecturas usan el miembro elegido y las escrituras dependen del rol del servidor.", "直接连接；读取使用所选成员，写入取决于服务器角色。")
        , ("consoleDatabaseAndScriptLimit", "Provide a database and a script up to 1 MB.", "Informe um banco e um script de até 1 MB.", "Indique una base de datos y un script de hasta 1 MB.", "请提供数据库和不超过 1 MB 的脚本。")
        , ("consoleDocumentLimit", "Limit: 1–1000 documents; timeout: 1–300000 ms.", "Limite: 1–1000 documentos; timeout: 1–300000 ms.", "Límite: 1–1000 documentos; tiempo de espera: 1–300000 ms.", "限制：1–1000 个文档；超时：1–300000 毫秒。")
        , ("consoleInvalidOperation", "Invalid operation.", "Operação inválida.", "Operación no válida.", "操作无效。")
        , ("consoleUnsupportedMethod", "Method is not supported.", "Método não suportado.", "Método no compatible.", "不支持此方法。")
        , ("consoleWriteNotConfirmed", "The write operation was not confirmed.", "Operação de escrita não confirmada.", "La operación de escritura no fue confirmada.", "写入操作未确认。")
        , ("consoleResultTooLarge", "Result exceeds 8 MB.", "Resultado excede 8 MB.", "El resultado supera 8 MB.", "结果超过 8 MB。")
        , ("consoleOutputLimit", "Output is limited to 100 results / 8 MB.", "Saída limitada a 100 resultados / 8 MB.", "La salida está limitada a 100 resultados / 8 MB.", "输出限制为 100 个结果 / 8 MB。")
        , ("consoleMessagesTooLarge", "Messages exceed 1 MB.", "Mensagens excedem 1 MB.", "Los mensajes superan 1 MB.", "消息超过 1 MB。")
        , ("consoleEnvironmentKeyMissing", "Key is not defined: ", "Chave não definida: ", "Clave no definida: ", "未定义的键：")
        , ("consoleUnknownUuidConstructor", "Unknown UUID constructor.", "Construtor UUID desconhecido.", "Constructor UUID desconocido.", "未知 UUID 构造函数。")
        , ("consoleExecutionCancelled", "Execution interrupted; server effects are not reverted.", "Execução interrompida; efeitos no servidor não são revertidos.", "Ejecución interrumpida; los efectos en el servidor no se revierten.", "执行已中断；服务器上的效果不会回滚。")
        , ("consoleExecutionTimedOut", "Timed out; effects sent to the server are not reverted.", "Tempo limite excedido; efeitos enviados ao servidor não são revertidos.", "Se agotó el tiempo; los efectos enviados al servidor no se revierten.", "已超时；发送到服务器的效果不会回滚。")
        , ("consoleHistoryNotSaved", "History was not saved: ", "Histórico não salvo: ", "No se guardó el historial: ", "历史记录未保存：")
        , ("consoleExpression", "Console · expression", "Console · expressão", "Consola · expresión", "控制台 · 表达式")
        , ("scriptMongosh", "mongosh script", "Script mongosh", "Script de mongosh", "mongosh 脚本")
        , ("resultAggregation", "Aggregation", "Agregação", "Agregación", "聚合")
        , ("resultQuery", "Query", "Consulta", "Consulta", "查询")
        , ("consoleHistoryItem", "{0} · {1} · {2} › {3}{4} · {5} · {6}", "{0} · {1} · {2} › {3}{4} · {5} · {6}", "{0} · {1} · {2} › {3}{4} · {5} · {6}", "{0} · {1} · {2} › {3}{4} · {5} · {6}")
        , ("consoleResultItem", "[{0}] {1} › {2}{3} · {4} document(s){5}", "[{0}] {1} › {2}{3} · {4} documento(s){5}", "[{0}] {1} › {2}{3} · {4} documento(s){5}", "[{0}] {1} › {2}{3} · {4} 个文档{5}")
        , ("modeConsole", "Console", "Console", "Consola", "控制台")
        , ("modeScript", "Script", "Script", "Script", "脚本")
        , ("modeAggregation", "Aggregation", "Agregação", "Agregación", "聚合")
        , ("modeQuery", "Query", "Consulta", "Consulta", "查询")
        , ("consoleInsertCount", "Insertion requires between 1 and 1000 documents.", "Inserção exige entre 1 e 1000 documentos.", "La inserción requiere entre 1 y 1000 documentos.", "插入需要 1 到 1000 个文档。")
        , ("consoleProtectedSystem", "System destination is protected.", "Destino de sistema protegido.", "El destino del sistema está protegido.", "系统目标受保护。")
        , ("consoleUpdateFilter", "An update operation requires a non-empty filter.", "Uma operação de alteração exige filtro não vazio.", "Una operación de actualización requiere un filtro no vacío.", "更新操作需要非空筛选条件。")
        , ("consoleProtectedIndex", "The required index is missing or global removal is protected.", "Índice obrigatório ou remoção global protegida.", "Falta el índice obligatorio o la eliminación global está protegida.", "缺少必需索引或全局删除受保护。")
        , ("consoleAggregateStages", "aggregate requires an array of stages.", "aggregate exige um array de estágios.", "aggregate requiere una matriz de etapas.", "aggregate 需要阶段数组。")
        , ("consoleUnsupportedOption", "Option is not supported: ", "Opção não suportada: ", "Opción no compatible: ", "不支持的选项：")
        , ("consoleNonNegativeSkipLimit", "Skip/limit requires a non-negative integer.", "Skip/limit exige inteiro não negativo.", "Skip/limit requiere un entero no negativo.", "Skip/limit 需要非负整数。")
        , ("suggestionsWithShortcut", "Suggestions ({0})", "Sugestões ({0})", "Sugerencias ({0})", "建议（{0}）")
        , ("suggestionsEllipsis", "Suggestions…", "Sugestões…", "Sugerencias…", "建议…")
        , ("suggestionShortcutHint", "Suggestion: {0} accepts; {1} dismisses", "Sugestão: {0} avança; {1} descarta", "Sugerencia: {0} acepta; {1} descarta", "建议：{0} 接受；{1} 忽略")
        , ("suggestionAccessible", "Suggestion", "Sugestão", "Sugerencia", "建议")
        , ("aiSuggestionShortcutHint", "Local AI suggestion · {0} accepts · {1} dismisses", "Sugestão da IA local · {0} insere · {1} descarta", "Sugerencia de IA local · {0} acepta · {1} descarta", "本地 AI 建议 · {0} 接受 · {1} 忽略")
        , ("spaceKey", "Space", "Espaço", "Espacio", "Space")
        , ("pageUpKey", "Page Up", "Page Up", "Página arriba", "Page Up")
        , ("pageDownKey", "Page Down", "Page Down", "Página abajo", "Page Down")
        , ("identifierPresentation", "Identifiers {0} · UUID {1}", "Identificadores {0} · UUID {1}", "Identificadores {0} · UUID {1}", "标识符 {0} · UUID {1}")
        , ("identifierStandardChoice", "Standard · ObjectId + UUID v4", "Padrão · ObjectId + UUID v4", "Estándar · ObjectId + UUID v4", "标准 · ObjectId + UUID v4")
        , ("identifierObjectIdChoice", "ObjectId · MongoDB ObjectId", "ObjectId · MongoDB ObjectId", "ObjectId · MongoDB ObjectId", "ObjectId · MongoDB ObjectId")
        , ("identifierUuidChoice", "UUID v4 · BSON subtype 4", "UUID v4 · BSON subtype 4", "UUID v4 · BSON subtype 4", "UUID v4 · BSON subtype 4")
        , ("identifierStandardDescription", "Accepts ObjectId and UUID v4. The IDE preserves the original BSON type and uses the most suitable representation for each identifier.", "Aceita ObjectId e UUID v4. A IDE preserva o tipo BSON original e utiliza a representação mais adequada para cada identificador.", "Acepta ObjectId y UUID v4. La IDE conserva el tipo BSON original y usa la representación más adecuada para cada identificador.", "接受 ObjectId 和 UUID v4。IDE 保留原始 BSON 类型，并为每个标识符使用最合适的表示形式。")
        , ("identifierObjectIdDescription", "Prioritizes MongoDB ObjectId as the default identifier representation. Existing UUIDs remain UUIDs in the representation selected below.", "Prioriza MongoDB ObjectId como representação padrão de identificadores. UUIDs existentes continuam UUID, na representação escolhida abaixo.", "Prioriza MongoDB ObjectId como representación predeterminada. Los UUID existentes siguen siendo UUID en la representación seleccionada abajo.", "优先使用 MongoDB ObjectId 作为默认标识符表示。现有 UUID 仍保持 UUID，并使用下方选择的表示形式。")
        , ("identifierUuidDescription", "Prioritizes BSON subtype 4 UUIDs. ObjectIds may be represented as UUIDs when needed without changing the stored ObjectId.", "Prioriza UUID padrão BSON subtype 4. ObjectIds podem ser representados como UUID quando a conversão for necessária, sem alterar o ObjectId gravado.", "Prioriza UUID BSON subtype 4. Los ObjectId pueden representarse como UUID cuando sea necesario, sin cambiar el ObjectId almacenado.", "优先使用 BSON subtype 4 UUID。必要时可将 ObjectId 表示为 UUID，但不会更改存储的 ObjectId。")
        , ("objectIdLabel", "ObjectId", "ObjectId", "ObjectId", "ObjectId")
        , ("objectIdBsonType", "BSON type ObjectId · 12 bytes", "Tipo BSON ObjectId · 12 bytes", "Tipo BSON ObjectId · 12 bytes", "BSON 类型 ObjectId · 12 字节")
        , ("hexLabel", "Hex", "Hex", "Hex", "十六进制")
        , ("hexDigits", "24 hexadecimal digits", "24 dígitos hexadecimais", "24 dígitos hexadecimales", "24 个十六进制数字")
        , ("equivalentUuidLabel", "Equivalent UUID", "UUID equivalente", "UUID equivalente", "等效 UUID")
        , ("uuidV4Label", "UUID v4", "UUID v4", "UUID v4", "UUID v4")
        , ("uuidPreviewDetails", "{0} · Binary subtype {1} · new UUIDs use this form; all four forms appear below", "{0} · Binary subtype {1} · novos UUIDs usam esta forma; as quatro formas aparecem abaixo", "{0} · Binary subtype {1} · los UUID nuevos usan esta forma; las cuatro formas aparecen abajo", "{0} · Binary subtype {1} · 新 UUID 使用此形式；下方显示全部四种形式")
        , ("mutationIdRequired", "The document must include _id. Run the query without excluding it from the projection.", "O documento deve incluir _id. Execute a consulta sem excluí-lo da projeção.", "El documento debe incluir _id. Ejecute la consulta sin excluirlo de la proyección.", "文档必须包含 _id。请不要在投影中排除它后重新运行查询。")
        , ("documentEditorIntro", "Document loaded from the result page; nothing was executed.", "Documento carregado da página de resultados; nada foi executado.", "Documento cargado desde la página de resultados; no se ejecutó nada.", "已从结果页加载文档；未执行任何操作。")
        , ("consoleDraftText", "Mongo Console JavaScript: db represents the selected database.", "Console JavaScript do Mongo: db representa o banco selecionado.", "JavaScript de la consola de Mongo: db representa la base de datos seleccionada.", "Mongo 控制台 JavaScript：db 代表所选数据库。")
        , ("identifierInterpretationInitial", "Paste ObjectId(\"…\"), 24 hexadecimal digits, UUID(\"…\")/CGUUID/JUUID/GUUID, or a UUID.", "Cole ObjectId(\"…\"), 24 dígitos hexadecimais, UUID(\"…\")/CGUUID/JUUID/GUUID ou um UUID.", "Pegue ObjectId(\"…\"), 24 dígitos hexadecimales, UUID(\"…\")/CGUUID/JUUID/GUUID o un UUID.", "粘贴 ObjectId(\"…\")、24 位十六进制数字、UUID(\"…\")/CGUUID/JUUID/GUUID 或 UUID。")
        , ("unknownLegacyUuidValue", "Binary subtype 3 · UUID of unknown origin", "Binary subtype 3 · UUID legado de origem desconhecida", "Binary subtype 3 · UUID heredado de origen desconocido", "Binary subtype 3 · 来源未知的旧版 UUID")
        , ("identifierObjectIdType", "ObjectId", "ObjectId", "ObjectId", "ObjectId")
        , ("identifierUuidType", "UUID · Binary subtype {0}", "UUID · Binary subtype {0}", "UUID · Binary subtype {0}", "UUID · Binary subtype {0}")
        , ("identifierTypeLine", "Type: {0} ({1})", "Tipo: {0} ({1})", "Tipo: {0} ({1})", "类型：{0}（{1}）")
        , ("identifierValueLine", "Value: {0}", "Valor: {0}", "Valor: {0}", "值：{0}")
        , ("identifierCanonicalLine", "Canonical Extended JSON: {0}", "Extended JSON canônico: {0}", "Extended JSON canónico: {0}", "规范 Extended JSON：{0}")
        , ("identifierFilterLine", "Filter: {{ _id: {0} }}", "Filtro: {{ _id: {0} }}", "Filtro: {{ _id: {0} }}", "过滤器：{{ _id: {0} }}")
        , ("identifierExplicit", "explicit", "explícito", "explícito", "显式")
        , ("identifierInferred", "inferred from text", "inferido do texto", "inferido del texto", "从文本推断")
        , ("scriptTextInitial", "// db starts at the connection's selected database\nconst filtro = EJSON.parse('{ \"ativo\": true }');\nconst cursor = db.getCollection(\"clientes\").find(filtro).limit(100);\nawait slop.results.stream(cursor);", "// db começa no banco selecionado da conexão\nconst filtro = EJSON.parse('{ \"ativo\": true }');\nconst cursor = db.getCollection(\"clientes\").find(filtro).limit(100);\nawait slop.results.stream(cursor);", "// db comienza en la base de datos seleccionada de la conexión\nconst filtro = EJSON.parse('{ \"ativo\": true }');\nconst cursor = db.getCollection(\"clientes\").find(filtro).limit(100);\nawait slop.results.stream(cursor);", "// db 从连接所选的数据库开始\nconst filtro = EJSON.parse('{ \"ativo\": true }');\nconst cursor = db.getCollection(\"clientes\").find(filtro).limit(100);\nawait slop.results.stream(cursor);")
        , ("operationLoadingProfiles", "Loading local connections", "Carregando conexões locais", "Cargando conexiones locales", "正在加载本地连接")
        , ("operationLoadingConsoleHistory", "Loading Console history", "Carregando histórico do Console", "Cargando el historial de la consola", "正在加载控制台历史记录")
        , ("operationLoadingTopology", "Loading topology", "Carregando topologia", "Cargando topología", "正在加载拓扑")
        , ("operationLoadingIndexes", "Loading indexes", "Carregando índices", "Cargando índices", "正在加载索引")
        , ("operationLoadingCollectionDetails", "Loading collection details", "Carregando detalhes da coleção", "Cargando detalles de la colección", "正在加载集合详细信息")
        , ("operationSavingEnvironments", "Saving environments", "Salvando ambientes", "Guardando entornos", "正在保存环境")
        , ("operationSavingConnection", "Saving connection", "Salvando conexão", "Guardando conexión", "正在保存连接")
        , ("operationLoadingHistory", "Loading history", "Carregando histórico", "Cargando historial", "正在加载历史记录")
        , ("operationLoadingScriptHistory", "Loading file history", "Carregando histórico de arquivos", "Cargando el historial de archivos", "正在加载文件历史记录")
        , ("operationLoadingSavedQueries", "Loading saved queries", "Carregando consultas salvas", "Cargando consultas guardadas", "正在加载已保存查询")
        , ("operationSavingQuery", "Saving query", "Salvando consulta", "Guardando consulta", "正在保存查询")
        , ("operationDeletingSavedQuery", "Deleting saved query", "Excluindo consulta salva", "Eliminando consulta guardada", "正在删除已保存查询")
        , ("operationLoadingDatabases", "Loading databases — {0}", "Carregando bancos — {0}", "Cargando bases de datos — {0}", "正在加载数据库 — {0}")
        , ("operationLoadingCollections", "Loading collections", "Carregando coleções", "Cargando colecciones", "正在加载集合")
        , ("operationLoadingServerStatus", "Loading server status", "Carregando estado do servidor", "Cargando estado del servidor", "正在加载服务器状态")
        , ("operationLoadingDatabaseStats", "Loading database statistics", "Carregando estatísticas do banco", "Cargando estadísticas de la base de datos", "正在加载数据库统计信息")
        , ("operationLoadingCollectionStats", "Loading collection statistics", "Carregando estatísticas da coleção", "Cargando estadísticas de la colección", "正在加载集合统计信息")
        , ("operationLoadingPage", "Loading page {0}", "Carregando página {0}", "Cargando página {0}", "正在加载页面 {0}")
        , ("operationSavingDocument", "Saving document", "Salvando documento", "Guardando documento", "正在保存文档")
        , ("operationSavingFile", "Saving file", "Salvando arquivo", "Guardando archivo", "正在保存文件")
        , ("operationCompletedSuffix", " — completed", " — concluído", " — completado", " — 已完成")
        , ("operationCancelledSuffix", " — canceled", " — cancelado", " — cancelado", " — 已取消")
        , ("operationConnectionFailedSuffix", " — failed; see the connection diagnostics", " — falha; consulte o diagnóstico da conexão", " — falló; consulte el diagnóstico de la conexión", " — 失败；请查看连接诊断")
        , ("operationFailedSuffix", " — failed; see the operation details", " — falha; consulte os detalhes da operação", " — falló; consulte los detalles de la operación", " — 失败；请查看操作详细信息")
        , ("identifierLabel", "Identifiers {0} · UUID {1} · subtype {2} · canonical Extended JSON below", "Identificadores {0} · UUID {1} · subtype {2} · Extended JSON canônico abaixo", "Identificadores {0} · UUID {1} · subtype {2} · Extended JSON canónico abajo", "标识符 {0} · UUID {1} · subtype {2} · 下方为规范 Extended JSON")
        , ("identifierGenerated", "Identifier generated in {0} mode; the constructor and Extended JSON write the same values.", "Identificador gerado no modo {0}; construtor e Extended JSON gravam os mesmos valores.", "Identificador generado en modo {0}; el constructor y Extended JSON escriben los mismos valores.", "已在 {0} 模式生成标识符；构造函数和 Extended JSON 写入相同值。")
        , ("uuidBsonTitle", "UUID representation · BSON Binary", "Representação UUID · Binary BSON", "Representación UUID · Binary BSON", "UUID 表示形式 · BSON Binary")
    ];

    static LocalizationViewModel()
    {
        foreach (var (key, en, pt, es, zh) in WorkspaceToolsTranslations.Concat(AgentTranslations).Concat(AgentAccountTranslations))
        {
            ((Dictionary<string, string>)Catalog["en"])[key] = en;
            ((Dictionary<string, string>)Catalog["pt-BR"])[key] = pt;
            ((Dictionary<string, string>)Catalog["es"])[key] = es;
            ((Dictionary<string, string>)Catalog["zh-CN"])[key] = zh;
        }
    }

    public static IReadOnlyCollection<string> TranslationKeys { get; } =
        Catalog[ApplicationLanguages.FallbackCode].Keys.ToArray();

    [ObservableProperty]
    private string _language = ApplicationLanguages.DefaultCode;

    public string this[string key] => Resolve(key);

    public bool HasTranslation(string key) =>
        Catalog.TryGetValue(ApplicationLanguages.Normalize(Language), out var selected) && selected.ContainsKey(key);

    public string LanguageLabel => this["language"];
    public string ThemeLabel => this["theme"];
    public string Connections => this["connections"];
    public string NewTab => this["newTab"];
    public string Open => this["open"];
    public string Save => this["save"];
    public string MoreActions => this["moreActions"];
    public string SaveAs => this["saveAs"];
    public string Preferences => this["preferences"];
    public string Databases => this["databases"];
    public string RefreshSelectedNode => this["refreshSelectedNode"];
    public string SearchLoadedItems => this["searchLoadedItems"];
    public string RefreshDatabases => this["refreshDatabases"];
    public string OpenManageConnections => this["openManageConnections"];
    public string NewScriptShortcut => this["newScriptShortcut"];
    public string OpenShortcut => this["openShortcut"];
    public string SaveShortcut => this["saveShortcut"];
    public string InterfaceLanguage => this["interfaceLanguage"];

    public string Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Catalog.TryGetValue(ApplicationLanguages.Normalize(Language), out var selected)
            && selected.TryGetValue(key, out var value)) return value;
        if (Catalog[ApplicationLanguages.FallbackCode].TryGetValue(key, out value)) return value;
        return "[[" + key + "]]";
    }

    public string ResolveOperationText(string source) => source switch
    {
        "Carregando conexões locais" => Resolve("operationLoadingProfiles"),
        "Carregando histórico do Console" => Resolve("operationLoadingConsoleHistory"),
        "Carregando topologia" => Resolve("operationLoadingTopology"),
        "Carregando índices" => Resolve("operationLoadingIndexes"),
        "Carregando detalhes da coleção" => Resolve("operationLoadingCollectionDetails"),
        "Salvando ambientes" => Resolve("operationSavingEnvironments"),
        "Salvando conexão" => Resolve("operationSavingConnection"),
        "Carregando histórico" => Resolve("operationLoadingHistory"),
        "Carregando histórico de arquivos" => Resolve("operationLoadingScriptHistory"),
        "Carregando consultas salvas" => Resolve("operationLoadingSavedQueries"),
        "Salvando consulta" => Resolve("operationSavingQuery"),
        "Excluindo consulta salva" => Resolve("operationDeletingSavedQuery"),
        "Carregando coleções" => Resolve("operationLoadingCollections"),
        "Carregando estado do servidor" => Resolve("operationLoadingServerStatus"),
        "Carregando estatísticas do banco" => Resolve("operationLoadingDatabaseStats"),
        "Carregando estatísticas da coleção" => Resolve("operationLoadingCollectionStats"),
        "Salvando documento" => Resolve("operationSavingDocument"),
        "Salvando arquivo" => Resolve("operationSavingFile"),
        " — concluído" => Resolve("operationCompletedSuffix"),
        " — cancelado" => Resolve("operationCancelledSuffix"),
        " — falha; consulte o diagnóstico da conexão" => Resolve("operationConnectionFailedSuffix"),
        " — falha; consulte os detalhes da operação" => Resolve("operationFailedSuffix"),
        _ when source.StartsWith("Carregando bancos — ", StringComparison.Ordinal)
            => Format("operationLoadingDatabases", source["Carregando bancos — ".Length..]),
        _ when source.StartsWith("Carregando página ", StringComparison.Ordinal)
            => Format("operationLoadingPage", source["Carregando página ".Length..]),
        _ => source
    };

    public string Format(string key, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Resolve(key), arguments);

    partial void OnLanguageChanged(string value)
    {
        var normalized = ApplicationLanguages.Normalize(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            Language = normalized;
            return;
        }

        OnPropertyChanged("Item[]");
        foreach (var key in Catalog.Values.SelectMany(dictionary => dictionary.Keys).Distinct(StringComparer.Ordinal))
            OnPropertyChanged("Item[" + key + "]");
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(NewTab));
        OnPropertyChanged(nameof(Open));
        OnPropertyChanged(nameof(Save));
        OnPropertyChanged(nameof(MoreActions));
        OnPropertyChanged(nameof(SaveAs));
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(Databases));
        OnPropertyChanged(nameof(RefreshSelectedNode));
        OnPropertyChanged(nameof(SearchLoadedItems));
        OnPropertyChanged(nameof(RefreshDatabases));
        OnPropertyChanged(nameof(OpenManageConnections));
        OnPropertyChanged(nameof(NewScriptShortcut));
        OnPropertyChanged(nameof(OpenShortcut));
        OnPropertyChanged(nameof(SaveShortcut));
        OnPropertyChanged(nameof(InterfaceLanguage));
    }
}
