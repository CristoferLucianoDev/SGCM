"""Genera sgcm_database.sql a partir de deploy.sql y las carpetas.
Uso: python build_single_file.py
"""
import re
from pathlib import Path

root = Path(__file__).parent
src = (root / "deploy.sql").read_text(encoding="utf-8")


def inline(match):
    path = match.group(1)
    body = (root / path).read_text(encoding="utf-8").rstrip() + "\n"
    return f"-- >>> {path}\n{body}"


out = re.sub(r"^\\ir (\S+)$", inline, src, flags=re.M)
out = out.replace(
    "-- deploy.sql: crea la base de datos completa (Modelo de Datos v1.3).",
    "-- sgcm_database.sql: todo deploy.sql en un solo archivo (Modelo de Datos v1.3).\n"
    "-- Generado con build_single_file.py; si cambias un script, vuelve a generarlo.",
)
out = out.replace("-f deploy.sql", "-f sgcm_database.sql")
(root / "sgcm_database.sql").write_text(out, encoding="utf-8")
print("sgcm_database.sql generado")
