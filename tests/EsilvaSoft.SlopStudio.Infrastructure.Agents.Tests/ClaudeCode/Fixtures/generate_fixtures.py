"""Gera as fixtures stream-json do CLI falso a partir dos transcripts sanitizados do spike P7-CL0-01.

Uso (na raiz do repositório): python tests/EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests/ClaudeCode/Fixtures/generate_fixtures.py

Adaptações documentadas: somente as linhas "out" (stdout) são mantidas; o session_id sanitizado vira {SESSION_ID};
assinaturas de thinking são truncadas; o system/init "adaptado" recebe tools == Read,Glob,Grep e mcp_servers == []
(o argv do produto usa --tools e --strict-mcp-config, que o spike confirmou restringirem exatamente essas listas);
a rodada Bash de perm-settings é removida em read-tools (Bash fica fora da allowlist) e mantida em unexpected-tool.
Pseudonimização (L7): IDs de mensagem (msg_), de ferramenta (toolu_), de requisição (req_) e uuids viram contadores
estáveis; nome do pipe de mensageria, caminhos de auto memória (com o fragmento do scratchpad e do repositório) e
qualquer menção ao repositório/scratchpad viram marcadores fixos.
Linhas iniciadas por '#' são diretivas do CLI falso (ver tests/EsilvaSoft.SlopStudio.FakeClaudeCode/Program.cs).
"""
import json
import os
import re

ROOT = os.path.dirname(os.path.abspath(__file__))
SPIKE = os.path.join(ROOT, *[".."] * 4, "eng", "spikes", "phase-07", "claude-code", "transcripts")
SESSION = re.compile(r"^[0-9a-f]{8}-…$")


def load(name):
    out = []
    with open(os.path.join(SPIKE, name), encoding="utf-8") as handle:
        for line in handle:
            record = json.loads(line)
            if record.get("s") == "out" and "json" in record:
                out.append(record["json"])
    return out


def scrub(value):
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            if key == "session_id" and isinstance(item, str):
                result[key] = "{SESSION_ID}"
            elif key == "signature" and isinstance(item, str):
                result[key] = item[:8] + "…" if item else item
            else:
                result[key] = scrub(item)
        return result
    if isinstance(value, list):
        return [scrub(item) for item in value]
    return value


def adapt_init(event, **overrides):
    if event.get("type") == "system" and event.get("subtype") == "init":
        event = dict(event)
        event["tools"] = ["Read", "Glob", "Grep"]
        event["mcp_servers"] = []
        event.update(overrides)
    return event


_PSEUDO = {}


def _pseudo(prefix, match):
    key = match.group(0)
    if key not in _PSEUDO:
        _PSEUDO[key] = "%s%04d" % (prefix, sum(1 for v in _PSEUDO.values() if v.startswith(prefix)) + 1)
    return _PSEUDO[key]


def pseudonymize(text):
    text = re.sub(r"msg_[A-Za-z0-9]+", lambda m: _pseudo("msg_fixture_", m), text)
    text = re.sub(r"toolu_[A-Za-z0-9]+", lambda m: _pseudo("toolu_fixture_", m), text)
    text = re.sub(r"req_[A-Za-z0-9]+", lambda m: _pseudo("req_fixture_", m), text)
    text = re.sub(r'"uuid":"[0-9a-f]{8}-…"', lambda m: '"uuid":"%s"' % _pseudo("uuid-", m), text)
    text = re.sub(r'"messaging_socket_path":"[^"]*"', '"messaging_socket_path":"<PIPE>"', text)
    text = re.sub(r'"memory_paths":\{[^}]*\}', '"memory_paths":{"auto":"<HOME>/.claude/projects/<WORK>/memory/"}', text)
    text = re.sub(r"[^\"]*(SlopDataAdimin|scratchpad)[^\"]*", "<REDACTED-PATH>", text, flags=re.IGNORECASE)
    return text


def write(name, events, prefix=(), suffix=()):
    with open(os.path.join(ROOT, name), "w", encoding="utf-8", newline="\n") as handle:
        for directive in prefix:
            handle.write(directive + "\n")
        for event in events:
            if isinstance(event, str):
                handle.write(event + "\n")
            else:
                handle.write(pseudonymize(json.dumps(event, ensure_ascii=False, separators=(",", ":"))) + "\n")
        for directive in suffix:
            handle.write(directive + "\n")


def is_init(event):
    return isinstance(event, dict) and event.get("type") == "system" and event.get("subtype") == "init"


def is_result(event):
    return isinstance(event, dict) and event.get("type") == "result"


basic1 = [scrub(e) for e in load("basic-turn1.jsonl")]
basic2 = [scrub(e) for e in load("basic-turn2-resume.jsonl")]
perm = [scrub(e) for e in load("perm-settings.jsonl")]

write("basic-turn1.jsonl", [adapt_init(e) for e in basic1])
write("basic-turn2-resume.jsonl", [adapt_init(e) for e in basic2])
write("basic-turn2-fragmented.jsonl", [adapt_init(e) for e in basic2], prefix=["#fragment"])

# Rodada Bash removida: da message_start da mensagem com tool_use Bash até o tool_result correspondente.
bash_ids = {b["id"] for e in perm if e.get("type") == "assistant" for b in e["message"]["content"]
            if b.get("type") == "tool_use" and b.get("name") == "Bash"}
bash_messages = {e["message"]["id"] for e in perm if e.get("type") == "assistant"
                 for b in e["message"]["content"] if b.get("id") in bash_ids}
read_tools, dropping = [], False
for event in perm:
    if event.get("type") == "stream_event" and event["event"].get("type") == "message_start" \
            and event["event"]["message"]["id"] in bash_messages:
        dropping = True
    if not dropping:
        read_tools.append(adapt_init(event))
    if dropping and event.get("type") == "user" and any(
            b.get("tool_use_id") in bash_ids for b in event["message"]["content"] if isinstance(b, dict)):
        dropping = False
# A negação do Bash sai de permission_denials junto com a rodada (negações de Read por deny não entram, spike).
read_tools = [dict(e, permission_denials=[]) if is_result(e) else e for e in read_tools]
write("read-tools.jsonl", read_tools)
write("unexpected-tool.jsonl", [adapt_init(e) for e in perm])

# init original (tools completas, conector claude.ai): divergência do argv do produto.
write("init-mismatch-tools.jsonl", basic1)
write("init-mismatch-apikey.jsonl", [adapt_init(e, apiKeySource="ANTHROPIC_API_KEY") if is_init(e) else e for e in basic1])
write("init-mismatch-permission.jsonl", [adapt_init(e, permissionMode="bypassPermissions") if is_init(e) else e for e in basic1])
write("init-old-version.jsonl", [adapt_init(e, claude_code_version="2.1.100") if is_init(e) else e for e in basic1])

# Até o init + filho de ferramenta + processo pendurado: cancelamento pela árvore.
head = []
for event in basic1:
    head.append(adapt_init(event))
    if is_init(event):
        break
write("cancel-with-child.jsonl", head, suffix=["#spawn-child", "#hang"])
write("hang-after-init.jsonl", head, suffix=["#hang"])

# Crash no meio do streaming (sem result): texto parcial e saída com código 3.
partial = [adapt_init(e) for e in basic2 if not is_result(e)][: len(basic2) - 6]
write("crash-mid-stream.jsonl", partial, suffix=["#stderr fake crash sk-ant-canario-stderr", "#exit 3"])

# Ruído antes do result: linha inválida e linha gigante são descartadas; o turno termina normalmente.
noisy = [adapt_init(e) for e in basic1]
result_index = next(i for i, e in enumerate(noisy) if is_result(e))
write("noise-then-result.jsonl", noisy[:result_index] + ["#garbage", "#giant 5000000"] + noisy[result_index:])
write("garbage-flood.jsonl", head, suffix=["#garbage"] * 12 + ["#hang"])

# Variações de result a partir do basic-turn1.
def with_result(**fields):
    events = []
    for event in basic1:
        event = adapt_init(event)
        if is_result(event):
            event = dict(event)
            event.update(fields)
        events.append(event)
    return events

write("result-max-turns.jsonl", with_result(subtype="error_max_turns", is_error=True, terminal_reason="max_turns"))
write("result-rate-limited.jsonl", with_result(subtype="error_during_execution", is_error=True, api_error_status=429))
write("result-auth-failed.jsonl", with_result(subtype="error_during_execution", is_error=True, api_error_status=401))
print("ok")

# Revisão P7-CL3-06.
# M2: quadro de subagente (parent_tool_use_id preenchido) logo depois do init.
subagent = [adapt_init(e) for e in basic1]
init_index = next(i for i, e in enumerate(subagent) if is_init(e))
frame = dict(next(e for e in subagent if isinstance(e, dict) and e.get("type") == "assistant"))
frame["parent_tool_use_id"] = "toolu_01SubagentParent"
write("subagent-frame.jsonl", subagent[: init_index + 1] + [frame] + subagent[init_index + 1:])
# L1: result de sucesso sem nenhum system/init antes.
write("result-without-init.jsonl", [e for e in basic1 if is_result(e) or e.get("type") == "rate_limit_event"])
# L2: result sem session_id.
write("result-without-session.jsonl", [adapt_init(e) if not is_result(e) else {k: v for k, v in e.items() if k != "session_id"}
                                        for e in basic1])
# M3: a CLI lê o prompt e morre antes do init.
write("exit-before-init.jsonl", [], suffix=["#exit 1"])
# Lacuna: filho cria um neto e sai; o neto fica órfão (sem pai vivo) e só o Job/grupo o alcança.
write("cancel-with-grandchild.jsonl", head, suffix=["#spawn-grandchild", "#hang"])
print("ok-revisao")
