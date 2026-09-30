param([string[]] $Roots = @('src', 'tests'))
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$patterns = [ordered]@{
    FileSystem = '\b(?:File|Directory)\.(?:Read\w*|Write\w*|Delete|Move|Copy|Exists|Open\w*|Create\w*|Get\w*|Enumerate\w*|Set\w*)\s*\(|\bnew\s+(?:global::)?(?:\w+\.)*(?:FileStream|FileInfo|DirectoryInfo|StreamReader|StreamWriter)\s*\('
    Process = '\bProcess\.Start\s*\(|\bnew\s+(?:global::)?(?:\w+\.)*(?:Process|ProcessStartInfo)\s*\('
    Console = '\bConsole\.(?:OpenStandard\w*|Read\w*|Write\w*|Set\w*)\s*\('
    HostState = '\bEnvironment\.(?:GetEnvironmentVariable|SetEnvironmentVariable|GetFolderPath)\b|\bEnvironment\.(?:CurrentDirectory|SystemDirectory|MachineName|UserName)\b|\bAppContext\.BaseDirectory\b|\bPath\.GetTemp(?:Path|FileName)\s*\(|\bPath\.GetFullPath\s*\((?:[^(),]|\([^()]*\))*\)'
    NativeSecurity = '\b(?:ProtectedData|WindowsIdentity)\.|\.(?:GetAccessControl|SetAccessControl|GetUnixFileMode|SetUnixFileMode)\s*\(|\[(?:DllImport|LibraryImport)\('
    ResourceSdk = '\bnew\s+(?:global::)?(?:\w+\.)*(?:LiteDatabase|LiteDbConnectionProfileRepository|SqliteConnection|CopilotClient|MongoClient|HttpClient|Socket|NamedPipeServerStream|NamedPipeClientStream|TcpListener)\s*\('
}
Push-Location $repositoryRoot
try {
    $rows = foreach ($category in $patterns.Keys) {
        $matches = & rg --json --glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' $patterns[$category] @Roots
        if ($LASTEXITCODE -gt 1) { throw "Falha na auditoria de $category." }
        foreach ($line in $matches) {
            $item = $line | ConvertFrom-Json
            if ($item.type -eq 'match') {
                [pscustomobject]@{ Path = $item.data.path.text.Replace('\', '/'); Line = $item.data.line_number; Category = $category }
            }
        }
    }
    $rows | Sort-Object Path, Line, Category | ConvertTo-Csv -Delimiter "`t" -NoTypeInformation
}
finally { Pop-Location }
