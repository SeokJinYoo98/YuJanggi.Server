#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${RUNNER_TEMP:?RUNNER_TEMP is required}"
: "${SSH_DIR:?SSH_DIR is required}"
staging_path=''

cleanup() {
    local original_status=$?
    local cleanup_status=0
    trap - EXIT
    set +e
    if [[ "$staging_path" =~ ^/tmp/yujanggi-deploy\.[A-Za-z0-9]+$ ]] && [[ -f "$SSH_DIR/key" && -f "$SSH_DIR/known_hosts" ]]; then
        ssh -i "$SSH_DIR/key" -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile="$SSH_DIR/known_hosts" "$SERVER_USER@$SERVER_HOST" "rm -rf -- '$staging_path'"
        cleanup_status=$?
    fi
    rm -f -- "$SSH_DIR/key" "$SSH_DIR/known_hosts" "$RUNNER_TEMP/yujanggi-publish.tar.gz"
    local local_cleanup_status=$?
    if (( cleanup_status == 0 )); then cleanup_status=$local_cleanup_status; fi
    if (( cleanup_status != 0 )); then echo 'Temporary deployment cleanup failed.' >&2; fi
    if (( original_status != 0 )); then exit "$original_status"; fi
    exit "$cleanup_status"
}
trap cleanup EXIT

: "${SERVER_HOST:?Set vars.SERVER_HOST}"
: "${SERVER_USER:?Set vars.SERVER_USER}"
: "${SERVER_DEPLOY_PATH:?Set vars.SERVER_DEPLOY_PATH}"
: "${SERVER_PRIVATE_KEY:?Set secrets.SERVER_PRIVATE_KEY}"
: "${SERVER_KNOWN_HOSTS:?Set secrets.SERVER_KNOWN_HOSTS with independently verified host keys}"
[[ "$SERVER_HOST" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]]
[[ "$SERVER_USER" =~ ^[a-z_][a-z0-9_-]*$ ]]
[[ "$SERVER_DEPLOY_PATH" =~ ^/[A-Za-z0-9_./-]+$ ]]
[[ "$SERVER_DEPLOY_PATH" != / && "/${SERVER_DEPLOY_PATH#/}/" != *'/../'* ]]
test -f artifacts/publish/linux-x64/YuJanggi.Server.dll
test -f artifacts/publish/linux-x64/YuJanggi.Server.deps.json
test -f artifacts/publish/linux-x64/YuJanggi.Server.runtimeconfig.json
umask 077
mkdir -p "$SSH_DIR"
printf '%s\n' "$SERVER_PRIVATE_KEY" | tr -d '\r' > "$SSH_DIR/key"
printf '%s\n' "$SERVER_KNOWN_HOSTS" > "$SSH_DIR/known_hosts"
chmod 600 "$SSH_DIR/key" "$SSH_DIR/known_hosts"
tar -czf "$RUNNER_TEMP/yujanggi-publish.tar.gz" -C artifacts/publish/linux-x64 .

staging_path=$(ssh -i "$SSH_DIR/key" -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile="$SSH_DIR/known_hosts" "$SERVER_USER@$SERVER_HOST" 'mktemp -d /tmp/yujanggi-deploy.XXXXXXXXXX')
[[ "$staging_path" =~ ^/tmp/yujanggi-deploy\.[A-Za-z0-9]+$ ]]
scp -i "$SSH_DIR/key" -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile="$SSH_DIR/known_hosts" "$RUNNER_TEMP/yujanggi-publish.tar.gz" "$SERVER_USER@$SERVER_HOST:$staging_path/publish.tar.gz"

set -euo pipefail
ssh -i "$SSH_DIR/key" -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile="$SSH_DIR/known_hosts" "$SERVER_USER@$SERVER_HOST" "bash -s -- '$staging_path' '$SERVER_DEPLOY_PATH'" <<'REMOTE'
set -euo pipefail
staging_path=$1
deploy_path=$2
[[ "$staging_path" =~ ^/tmp/yujanggi-deploy\.[A-Za-z0-9]+$ ]]
test -d "$deploy_path"
deploy_path=$(realpath -e -- "$deploy_path")
case "$deploy_path" in /|/home|/opt|/usr|/var|/tmp|/etc) echo 'Refusing deployment to a system directory.' >&2; exit 1 ;; esac
mkdir "$staging_path/publish"
tar -xzf "$staging_path/publish.tar.gz" -C "$staging_path/publish"
test -f "$staging_path/publish/YuJanggi.Server.dll"
sudo -n systemctl stop yujanggi
# Preserve unrelated configuration/log files; do not delete the destination.
sudo -n cp -R --preserve=mode,timestamps -- "$staging_path/publish/." "$deploy_path/"
sudo -n systemctl restart yujanggi
sudo -n systemctl is-active --quiet yujanggi
REMOTE
