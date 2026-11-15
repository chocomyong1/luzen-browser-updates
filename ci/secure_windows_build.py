"""Windows CI helper: decrypt a one-time encrypted source bundle into runner-only disk."""
import pathlib
import sys
import zipfile

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding
from cryptography.hazmat.primitives.ciphers.aead import AESGCM


def main():
    if len(sys.argv) != 4:
        raise SystemExit("usage: secure_windows_build.py private.pem source.enc destination")
    private_pem = pathlib.Path(sys.argv[1]).read_bytes()
    sealed = pathlib.Path(sys.argv[2]).read_bytes()
    if not sealed.startswith(b"LZNB1"):
        raise ValueError("Invalid sealed source header")
    key_len = int.from_bytes(sealed[5:7], "big")
    if key_len < 256 or key_len > 1024:
        raise ValueError("Invalid sealed key length")
    key_blob = sealed[7:7 + key_len]
    nonce = sealed[7 + key_len:19 + key_len]
    ciphertext = sealed[19 + key_len:]
    private = serialization.load_pem_private_key(private_pem, password=None)
    symmetric_key = private.decrypt(
        key_blob,
        padding.OAEP(mgf=padding.MGF1(algorithm=hashes.SHA256()), algorithm=hashes.SHA256(), label=None),
    )
    decrypted = AESGCM(symmetric_key).decrypt(nonce, ciphertext, b"LuzenBrowser-Windows-Build-v1")
    destination = pathlib.Path(sys.argv[3]).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    zip_path = destination.parent / "temp-decrypted-source.zip"
    try:
        zip_path.write_bytes(decrypted)
        with zipfile.ZipFile(zip_path) as archive:
            for member in archive.infolist():
                target = (destination / member.filename).resolve()
                if not target.is_relative_to(destination):
                    raise ValueError("Unexpected archive path")
            archive.extractall(destination)
    finally:
        zip_path.unlink(missing_ok=True)
    if not (destination / "AppSource" / "LuzenBrowserReplica.csproj").is_file():
        raise ValueError("Expected project missing")
    print("Protected source decrypted in the temporary Windows build environment.")


if __name__ == "__main__":
    main()
