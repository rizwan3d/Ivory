#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="${HOME}/.tusk/bin"

remove_path_line() {
  local file="$1"
  [ -f "${file}" ] || return 0
  local tmp
  tmp="$(mktemp)"
  # Strip lines we added that contain the install dir or our marker comment.
  grep -vF "${INSTALL_DIR}" "${file}" | grep -vF "# Added by Tusk installer" > "${tmp}" || true
  mv "${tmp}" "${file}"
}

echo "Removing Tusk binary from ${INSTALL_DIR}..."
rm -f "${INSTALL_DIR}/tusk"

# Clean up empty directory tree if nothing else is there.
if [ -d "${INSTALL_DIR}" ] && [ -z "$(ls -A "${INSTALL_DIR}")" ]; then
  rmdir "${INSTALL_DIR}"
fi
if [ -d "${HOME}/.tusk" ] && [ -z "$(ls -A "${HOME}/.tusk")" ]; then
  rmdir "${HOME}/.tusk"
fi

for profile in "${HOME}/.profile" "${HOME}/.zprofile" "${HOME}/.zshrc"; do
  remove_path_line "${profile}"
done

echo "Tusk uninstalled. Restart your shell to ensure PATH changes are applied."
