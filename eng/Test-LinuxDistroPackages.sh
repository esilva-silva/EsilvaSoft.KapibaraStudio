#!/usr/bin/env bash
# Teste de instalação, atualização, rollback e remoção dos pacotes Linux (deb, rpm, pacman).
# Executar como root dentro da distribuição/container alvo (sem sudo).
#
# Uso:
#   Test-LinuxDistroPackages.sh --format deb|rpm|pacman --package <v1> [--upgrade <v2>]
#                               [--run-seconds 20] [--user <usuário não-root>]
#
# Requisitos no ambiente: o gerenciador do formato, runuser, timeout e, sem DISPLAY/WAYLAND_DISPLAY,
# xvfb-run (com xauth). desktop-file-validate é opcional. Nada é baixado além do que o gerenciador resolve.
set -euo pipefail

FORMAT=''
PACKAGE=''
UPGRADE=''
RUN_SECONDS=20
APP_USER='kapibara-test'

CURRENT_STEP='início'
PASSED=0
ROLLBACK_METHOD='n/a'

step() { CURRENT_STEP="$*"; echo "== $* =="; }
info() { echo "   $*"; }
fail() { echo "FALHA: $*" >&2; exit 1; }
usage() {
    sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//' >&2
    exit 2
}

on_exit() {
    local code=$?
    if [ "$PASSED" -ne 1 ]; then
        echo "RESULT: FAIL (etapa: ${CURRENT_STEP}; código ${code})" >&2
    fi
}
trap on_exit EXIT

while [ $# -gt 0 ]; do
    case "$1" in
        --format) FORMAT="${2:-}"; shift 2 ;;
        --package) PACKAGE="${2:-}"; shift 2 ;;
        --upgrade) UPGRADE="${2:-}"; shift 2 ;;
        --run-seconds) RUN_SECONDS="${2:-}"; shift 2 ;;
        --user) APP_USER="${2:-}"; shift 2 ;;
        -h|--help) usage ;;
        *) echo "Argumento desconhecido: $1" >&2; usage ;;
    esac
done

[ "$(id -u)" -eq 0 ] || fail 'execute como root (o script não usa sudo).'
case "$FORMAT" in deb|rpm|pacman) ;; *) echo 'Informe --format deb|rpm|pacman.' >&2; usage ;; esac
[ -n "$PACKAGE" ] || { echo 'Informe --package.' >&2; usage; }
[[ "$RUN_SECONDS" =~ ^[0-9]+$ ]] && [ "$RUN_SECONDS" -gt 0 ] || fail '--run-seconds deve ser um inteiro positivo.'
[ -f "$PACKAGE" ] || fail "pacote inexistente: $PACKAGE"
[ -z "$UPGRADE" ] || [ -f "$UPGRADE" ] || fail "pacote de upgrade inexistente: $UPGRADE"

case "$FORMAT" in
    deb) PKG_NAME=kapibarastudio; MARKER=apt ;;
    rpm) PKG_NAME=kapibarastudio; MARKER=dnf ;;
    pacman) PKG_NAME=kapibarastudio-bin; MARKER=pacman ;;
esac

LIB_DIR=/usr/lib/kapibarastudio
EXE="$LIB_DIR/EsilvaSoft.KapibaraStudio.Desktop"
BIN=/usr/bin/kapibarastudio
ICON_SIZES='16 24 32 48 64 128 256 512'

# Cópia dos pacotes em um diretório legível por todos (o apt acessa o arquivo como usuário _apt).
WORK_DIR="$(mktemp -d)"
chmod 755 "$WORK_DIR"
LOG_DIR="$WORK_DIR/logs"
mkdir -p "$LOG_DIR"
# Em caso de falha os logs do app são preservados em $WORK_DIR para diagnóstico.
cleanup() {
    if [ "$PASSED" -eq 1 ]; then
        rm -rf "$WORK_DIR" "${RUN_HOME:-}"
    else
        echo "Logs preservados em $LOG_DIR (HOME de teste: ${RUN_HOME:-n/a})" >&2
    fi
}
trap 'on_exit; cleanup' EXIT

stage_package() { # origem -> caminho absoluto copiado
    local dest
    dest="$WORK_DIR/$(basename "$1")"
    cp -- "$1" "$dest"
    chmod 644 "$dest"
    printf '%s' "$dest"
}
PKG_V1="$(stage_package "$PACKAGE")"
PKG_V2=''
[ -z "$UPGRADE" ] || PKG_V2="$(stage_package "$UPGRADE")"

# ---------------------------------------------------------------------------------------------------------
# Operações do gerenciador
# ---------------------------------------------------------------------------------------------------------
prepare_manager() {
    case "$FORMAT" in
        deb) export DEBIAN_FRONTEND=noninteractive; apt-get update ;;
        rpm) : ;;
        pacman) pacman -Syu --noconfirm ;;
    esac
}

pm_install() { # instala (ou atualiza) a partir de um arquivo local, resolvendo dependências do repositório
    case "$FORMAT" in
        deb) apt-get install -y "$1" ;;
        rpm) dnf install -y "$1" ;;
        pacman) pacman -U --noconfirm "$1" ;;
    esac
}

pm_upgrade() {
    case "$FORMAT" in
        deb) apt-get install -y "$1" ;;
        rpm) dnf upgrade -y "$1" ;;
        pacman) pacman -U --noconfirm "$1" ;;
    esac
}

pm_downgrade() { # rollback para o arquivo $1
    case "$FORMAT" in
        deb) apt-get install -y --allow-downgrades "$1" ;;
        rpm) dnf downgrade -y "$1" ;;
        pacman) pacman -U --noconfirm "$1" ;;
    esac
}

installed_version() {
    case "$FORMAT" in
        deb) dpkg-query -W -f='${Version}' "$PKG_NAME" ;;
        rpm) rpm -q --qf '%{VERSION}-%{RELEASE}' "$PKG_NAME" ;;
        pacman) pacman -Q "$PKG_NAME" | awk '{print $2}' ;;
    esac
}

file_version() { # versão declarada no arquivo do pacote, no mesmo formato de installed_version
    case "$FORMAT" in
        deb) dpkg-deb -f "$1" Version ;;
        rpm) rpm -qp --qf '%{VERSION}-%{RELEASE}' "$1" ;;
        pacman) pacman -Qp "$1" | awk '{print $2}' ;;
    esac
}

assert_installed_version() { # arquivo
    local expected actual
    expected="$(file_version "$1")"
    actual="$(installed_version)"
    [ -n "$expected" ] || fail "não foi possível ler a versão de $1"
    [ "$expected" = "$actual" ] || fail "versão instalada '$actual' difere da esperada '$expected' ($1)"
    info "versão instalada: $actual"
}

# ---------------------------------------------------------------------------------------------------------
# Verificações
# ---------------------------------------------------------------------------------------------------------
check_installation() {
    local size icon
    [ -L "$BIN" ] || fail "$BIN não é um link simbólico"
    [ "$(readlink "$BIN")" = "$EXE" ] || fail "$BIN aponta para '$(readlink "$BIN")', esperado $EXE"
    [ -f "$EXE" ] && [ -x "$EXE" ] || fail "$EXE ausente ou não executável"
    [ "$(stat -c %a "$EXE")" = 755 ] || fail "$EXE com modo $(stat -c %a "$EXE"), esperado 755"
    [ -f "$LIB_DIR/package-manager" ] || fail "marcador $LIB_DIR/package-manager ausente"
    [ "$(cat "$LIB_DIR/package-manager")" = "$MARKER" ] ||
        fail "marcador package-manager = '$(cat "$LIB_DIR/package-manager")', esperado '$MARKER'"
    [ -s "$LIB_DIR/LICENSE" ] || fail "$LIB_DIR/LICENSE ausente"
    [ -s "$LIB_DIR/THIRD-PARTY-NOTICES.md" ] || fail "$LIB_DIR/THIRD-PARTY-NOTICES.md ausente"
    [ -s /usr/share/applications/kapibarastudio.desktop ] || fail 'arquivo .desktop ausente'
    if command -v desktop-file-validate >/dev/null 2>&1; then
        desktop-file-validate /usr/share/applications/kapibarastudio.desktop || fail 'desktop-file-validate reprovou o .desktop'
    else
        info 'AVISO: desktop-file-validate ausente; .desktop não validado'
    fi
    for size in $ICON_SIZES; do
        icon="/usr/share/icons/hicolor/${size}x${size}/apps/kapibarastudio.png"
        [ -s "$icon" ] || fail "ícone ausente: $icon"
    done
    case "$FORMAT" in
        deb) [ -s /usr/share/doc/kapibarastudio/copyright ] || fail 'copyright do deb ausente' ;;
        rpm) [ -s /usr/share/licenses/kapibarastudio/LICENSE ] || fail 'licença do rpm ausente' ;;
        pacman) [ -s "/usr/share/licenses/$PKG_NAME/LICENSE" ] || fail 'licença do Arch ausente' ;;
    esac
}

# Usuário não-root e diretórios temporários de execução (HOME único para toda a sequência).
if ! id "$APP_USER" >/dev/null 2>&1; then
    useradd -m -s /bin/bash "$APP_USER"
fi
RUN_HOME="$(mktemp -d)"
chown "$APP_USER" "$RUN_HOME"
chmod 700 "$RUN_HOME"
DATA_ROOT="$RUN_HOME/.local/share"
DATA_DIR="$DATA_ROOT/EsilvaSoft/KapibaraStudio"
RUN_INDEX=0

check_native_libraries() {
    local extract_dir="$RUN_HOME/.net" missing=0 count=0 lib out
    if [ -d "$extract_dir" ]; then
        while IFS= read -r -d '' lib; do
            count=$((count + 1))
            out="$(LD_LIBRARY_PATH="$(dirname "$lib")" ldd "$lib" 2>&1 || true)"
            if printf '%s\n' "$out" | grep -q 'not found'; then
                echo "   dependências ausentes em $lib:" >&2
                printf '%s\n' "$out" | grep 'not found' >&2
                missing=1
            fi
        done < <(find "$extract_dir" -type f \( -name '*.so' -o -name '*.so.*' \) -print0)
    fi
    out="$(ldd "$EXE" 2>&1 || true)"
    if printf '%s\n' "$out" | grep -q 'not found'; then
        echo "   dependências ausentes em $EXE:" >&2
        printf '%s\n' "$out" | grep 'not found' >&2
        missing=1
    fi
    info "bibliotecas nativas extraídas verificadas com ldd: $count"
    [ "$count" -gt 0 ] || info 'AVISO: nenhuma .so extraída em ~/.net (bundle pode não extrair nativas)'
    [ "$missing" -eq 0 ] || fail 'ldd encontrou bibliotecas "not found"'
}

run_app() { # rótulo
    local label="$1" log code=0 display_cmd=()
    RUN_INDEX=$((RUN_INDEX + 1))
    log="$LOG_DIR/run-${RUN_INDEX}-${label}.log"
    # Avalonia usa X11: só WAYLAND_DISPLAY não basta, então sem DISPLAY o app roda no Xvfb.
    if [ -z "${DISPLAY:-}" ]; then
        command -v xvfb-run >/dev/null 2>&1 || fail 'sem DISPLAY e xvfb-run ausente'
        display_cmd=(xvfb-run -a -s '-screen 0 1280x1024x24')
    fi
    info "executando o app como '$APP_USER' por ${RUN_SECONDS}s (log: $log)"
    # xvfb-run fica por fora do timeout para encerrar o X junto; o código 124 do timeout é repassado.
    runuser -u "$APP_USER" -- env \
        HOME="$RUN_HOME" XDG_DATA_HOME="$DATA_ROOT" XDG_CONFIG_HOME="$RUN_HOME/.config" \
        XDG_CACHE_HOME="$RUN_HOME/.cache" DOTNET_BUNDLE_EXTRACT_BASE_DIR="$RUN_HOME/.net" \
        ${DISPLAY:+DISPLAY="$DISPLAY"} ${WAYLAND_DISPLAY:+WAYLAND_DISPLAY="$WAYLAND_DISPLAY"} \
        ${XDG_RUNTIME_DIR:+XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR"} \
        "${display_cmd[@]}" timeout -k 5s "${RUN_SECONDS}s" "$BIN" >"$log" 2>&1 || code=$?
    if [ "$code" -ne 124 ]; then
        echo "--- últimas linhas do log ---" >&2
        tail -n 40 "$log" >&2 || true
        [ "$code" -ne 137 ] || fail "o app estava vivo após ${RUN_SECONDS}s, mas não encerrou com SIGTERM em 5s (código 137)"
        fail "o app terminou antes do prazo ou falhou (código $code; esperado 124 = ainda vivo após ${RUN_SECONDS}s)"
    fi
    info 'app permaneceu em execução até o timeout (código 124)'
    check_native_libraries
    [ -f "$DATA_DIR/workspace.db" ] || { tail -n 40 "$log" >&2 || true; fail "workspace.db não foi criado em $DATA_DIR"; }
    verify_managed_files
}

verify_managed_files() { # o app nunca altera arquivos que pertencem ao gerenciador de pacotes
    local out
    case "$FORMAT" in
        deb) out="$(dpkg --verify "$PKG_NAME" 2>&1)" || true ;;
        rpm) out="$(rpm -V "$PKG_NAME" 2>&1)" || true ;;
        pacman) out="$(pacman -Qkk "$PKG_NAME" 2>&1 | grep -v ' 0 altered files' || true)" ;;
    esac
    [ -z "$out" ] || { printf '%s\n' "$out" >&2; fail 'arquivos gerenciados pelo pacote foram alterados após executar o app'; }
    info 'arquivos gerenciados intactos após executar o app'
}

snapshot_data() { # imprime sha256 de todos os arquivos do diretório de dados, em ordem estável
    [ -d "$DATA_DIR" ] || fail "diretório de dados ausente: $DATA_DIR"
    (cd "$DATA_DIR" && find . -type f -print0 | LC_ALL=C sort -z | xargs -0 -r sha256sum)
}

assert_data_unchanged() { # snapshot esperado, rótulo
    local now
    now="$(snapshot_data)"
    if [ "$now" != "$1" ]; then
        diff <(printf '%s\n' "$1") <(printf '%s\n' "$now") >&2 || true
        fail "diretório de dados do usuário mudou $2"
    fi
    info "diretório de dados intacto $2"
}

assert_removed() {
    [ ! -e "$LIB_DIR" ] || fail "$LIB_DIR ainda existe após a remoção"
    [ ! -e "$BIN" ] && [ ! -L "$BIN" ] || fail "$BIN ainda existe após a remoção"
}

# ---------------------------------------------------------------------------------------------------------
# Sequência
# ---------------------------------------------------------------------------------------------------------
step 'preparar o gerenciador de pacotes'
prepare_manager

step "instalar v1 ($(basename "$PKG_V1"))"
pm_install "$PKG_V1"

step 'conferir instalação v1'
check_installation
assert_installed_version "$PKG_V1"

step 'executar o app (v1)'
run_app v1

step 'registrar hashes do diretório de dados'
DATA_V1="$(snapshot_data)"
printf '%s\n' "$DATA_V1" | sed 's/^/   /'

if [ -n "$PKG_V2" ]; then
    step "atualizar para v2 ($(basename "$PKG_V2"))"
    pm_upgrade "$PKG_V2"
    check_installation
    assert_installed_version "$PKG_V2"
    assert_data_unchanged "$DATA_V1" 'após o upgrade'

    step 'executar o app (v2)'
    run_app v2
    DATA_V2="$(snapshot_data)"

    step 'rollback para v1'
    case "$FORMAT" in
        rpm)
            if dnf history undo last -y; then
                if [ "$(installed_version)" = "$(file_version "$PKG_V1")" ]; then
                    ROLLBACK_METHOD='dnf history undo last'
                else
                    info 'dnf history undo last terminou mas a versão não voltou; usando dnf downgrade'
                    pm_downgrade "$PKG_V1"
                    ROLLBACK_METHOD='dnf downgrade (undo não restaurou a versão)'
                fi
            else
                info 'dnf history undo last falhou (esperado sem o pacote antigo em repositório); usando dnf downgrade'
                pm_downgrade "$PKG_V1"
                ROLLBACK_METHOD='dnf downgrade (undo falhou)'
            fi
            ;;
        deb) pm_downgrade "$PKG_V1"; ROLLBACK_METHOD='apt-get install --allow-downgrades' ;;
        pacman) pm_downgrade "$PKG_V1"; ROLLBACK_METHOD='pacman -U' ;;
    esac
    info "método de rollback: $ROLLBACK_METHOD"
    check_installation
    assert_installed_version "$PKG_V1"
    assert_data_unchanged "$DATA_V2" 'após o rollback'

    step 'executar o app após o rollback'
    run_app rollback
fi

step 'remover o pacote'
DATA_BEFORE_REMOVE="$(snapshot_data)"
case "$FORMAT" in
    deb)
        apt-get remove -y "$PKG_NAME"
        assert_removed
        # Without conffiles dpkg forgets the package on remove; purge only applies to the "rc" state.
        if dpkg-query -W -f='${Status}' "$PKG_NAME" 2>/dev/null | grep -q 'config-files'; then
            apt-get purge -y "$PKG_NAME"
        else
            info 'nenhum resíduo de configuração para purge (pacote sem conffiles)'
        fi
        assert_removed
        ;;
    rpm) dnf remove -y "$PKG_NAME"; assert_removed ;;
    pacman) pacman -R --noconfirm "$PKG_NAME"; assert_removed ;;
esac
assert_data_unchanged "$DATA_BEFORE_REMOVE" 'após a remoção'

step 'resumo'
info "formato: $FORMAT"
info "pacote v1: $(basename "$PKG_V1")"
[ -z "$PKG_V2" ] || info "pacote v2: $(basename "$PKG_V2") (rollback: $ROLLBACK_METHOD)"
info 'logs e HOME temporário serão removidos (execução bem-sucedida)'
PASSED=1
echo 'RESULT: PASS'
