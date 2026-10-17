"""Structural TRX diagnostics; never return messages, stdout or assertion values."""

import re
from functools import lru_cache
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
EXCEPTIONS = (
    "Npgsql.PostgresException",
    "Npgsql.NpgsqlException",
    "Microsoft.EntityFrameworkCore.DbUpdateException",
    "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException",
    "MediPOS.Application.Errors.ApplicationErrorException",
    "System.InvalidOperationException",
    "System.ArgumentException",
    "System.ArgumentOutOfRangeException",
    "System.UnauthorizedAccessException",
    "System.NotSupportedException",
    "System.FormatException",
    "System.TimeoutException",
    "System.OperationCanceledException",
    "System.Threading.Tasks.TaskCanceledException",
    "Xunit.Sdk.ThrowsException",
    "Xunit.Sdk.EqualException",
    "Xunit.Sdk.SingleException",
    "Xunit.Sdk.EmptyException",
    "Xunit.Sdk.TrueException",
    "Xunit.Sdk.FalseException",
    "Xunit.Sdk.ContainsException",
)
SQLSTATES = {
    "08001", "08006", "0A000", "22001", "22003", "22007", "22008", "22023",
    "22P02", "23502", "23503", "23505", "23514", "25P02", "28P01", "40001",
    "40P01", "42501", "42601", "42703", "42804", "42883", "42P01", "55000",
    "55P03", "57014", "57P01", "53300", "P0001",
}


@lru_cache(maxsize=4)
def source_metadata(backend_root):
    files = [path for folder in ("src", "tests", "tools")
             for path in (backend_root / folder).rglob("*.cs")
             if not {"bin", "obj"}.intersection(path.relative_to(backend_root).parts)]
    known, constraints = {}, set()
    for path in files:
        source = path.read_text(encoding="utf-8-sig")
        known[path.relative_to(backend_root).as_posix()] = len(source.splitlines())
        constraints.update(re.findall(r"(?<!\w)(?:ck_|FK_|ux_|IX_)[A-Za-z0-9_~]+", source))
    return known, constraints


def failure_diagnostics(result, backend_root):
    message = result.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=NS)
    stack = result.findtext("t:Output/t:ErrorInfo/t:StackTrace", default="", namespaces=NS)
    detail = message + "\n" + stack  # Do not inspect StdOut or theory display names.
    types = [name for name in EXCEPTIONS if re.search(r"(?<![\w.])" + re.escape(name) + r"(?![\w.])", detail)]
    diagnostics = ["exception_types=" + (",".join(types) if types else "UNCLASSIFIED")]
    states = re.findall(r"Npgsql\.PostgresException(?:\s+\(0x[0-9A-Fa-f]+\))?\s*:\s*([0-9A-Z]{5}):", message)
    states = sorted(set(states) & SQLSTATES)
    if states:
        diagnostics.append("sqlstate=" + ",".join(states))
    # Only existing repository C# locations, never absolute paths or arbitrary identifiers.
    backend_root = Path(backend_root).resolve()
    known, constraints = source_metadata(backend_root)
    names = re.findall(r"\bconstraint [\"']([^\"'\r\n]+)[\"']", message, re.IGNORECASE)
    for name in sorted(set(names) & constraints)[:4]:
        diagnostics.append("constraint=" + name)
    locations = re.findall(r"/backend/((?:src|tests|tools)/[A-Za-z0-9_./-]+\.cs):line ([0-9]{1,7})(?![0-9])", stack.replace("\\", "/"))
    emitted = set()
    for path, line in locations:
        location = path + ":" + line
        if path in known and 1 <= int(line) <= known[path] and location not in emitted:
            diagnostics.append("source=" + location)
            emitted.add(location)
        if len(emitted) == 5:
            break
    if not emitted:
        diagnostics.append("source=UNAVAILABLE")
    return diagnostics
