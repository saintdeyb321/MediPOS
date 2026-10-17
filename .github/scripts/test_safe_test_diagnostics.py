import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from safe_test_diagnostics import NS, failure_diagnostics


class SafeFailureDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        source = self.root / "src/Module/Handler.cs"
        source.parent.mkdir(parents=True)
        source.write_text('// synthetic source location; constraint "ck_demo_integrity"\n' + "// fixture line\n" * 100, encoding="utf-8")

    def result(self, message="", stack="", stdout=""):
        def element(name, parent):
            return ET.SubElement(parent, "{" + NS["t"] + "}" + name)
        result = ET.Element("{" + NS["t"] + "}UnitTestResult")
        output = element("Output", result)
        info = element("ErrorInfo", output)
        element("Message", info).text = message
        element("StackTrace", info).text = stack
        element("StdOut", output).text = stdout
        return result

    def test_only_allowlisted_exception_types_and_sqlstate_are_returned(self):
        output = failure_diagnostics(self.result(
            "Microsoft.EntityFrameworkCore.DbUpdateException ---> Npgsql.PostgresException (0x80004005): 23514: private row contents"), self.root)
        self.assertIn("sqlstate=23514", output)
        self.assertIn("Npgsql.PostgresException", output[0])
        self.assertNotIn("private row contents", " ".join(output))

    def test_assertion_values_and_sensitive_fields_are_never_returned(self):
        secrets = ("Password=synthetic-secret;Host=private.invalid", "cookie-value", "oidc-token", "private-email@example.invalid")
        output = failure_diagnostics(self.result("Xunit.Sdk.EqualException Expected: " + repr(secrets)), self.root)
        self.assertIn("Xunit.Sdk.EqualException", output[0])
        for secret in secrets:
            self.assertNotIn(secret, " ".join(output))

    def test_stdout_and_display_name_are_not_diagnostic_sources(self):
        result = self.result(stdout="Npgsql.PostgresException: 23505: secret")
        result.set("testName", "Npgsql.PostgresException: 42601: private-theory-argument")
        self.assertEqual(["exception_types=UNCLASSIFIED", "source=UNAVAILABLE"], failure_diagnostics(result, self.root))

    def test_existing_repo_locations_are_relative_and_bounded(self):
        stack = "\n".join("at Call() in /home/runner/work/MediPOS/MediPOS/backend/src/Module/Handler.cs:line " + str(i) for i in range(1, 9))
        output = failure_diagnostics(self.result(stack=stack), self.root)
        self.assertEqual(5, sum(value.startswith("source=") for value in output))
        self.assertIn("source=src/Module/Handler.cs:1", output)
        self.assertNotIn("/home/runner", " ".join(output))

    def test_windows_paths_are_supported_without_disclosing_user_directory(self):
        output = failure_diagnostics(self.result(stack=r"at Call() in C:\Users\Private\backend\src\Module\Handler.cs:line 42"), self.root)
        self.assertIn("source=src/Module/Handler.cs:42", output)
        self.assertNotIn("Private", " ".join(output))

    def test_unrecognized_types_codes_paths_and_path_traversal_are_not_echoed(self):
        output = failure_diagnostics(self.result(
            message="System.SuperSecretException Npgsql.PostgresException: TOKEN: sensitive",
            stack="at Call() in /backend/src/../private.cs:line 9\nat Call() in /backend/src/Secret.cs:line 5\nat Call() in /backend/src/Module/Handler.cs:line 9999999"), self.root)
        self.assertNotIn("sqlstate=TOKEN", output)
        self.assertEqual("source=UNAVAILABLE", output[-1])
        self.assertNotIn("SuperSecret", " ".join(output))

    def test_missing_error_info_remains_explicitly_unclassified(self):
        self.assertEqual(["exception_types=UNCLASSIFIED", "source=UNAVAILABLE"], failure_diagnostics(ET.Element("result"), self.root))

    def test_duplicate_locations_are_reported_once(self):
        frame = "at Call() in /backend/src/Module/Handler.cs:line 12\n"
        output = failure_diagnostics(self.result(stack=frame * 3), self.root)
        self.assertEqual(1, output.count("source=src/Module/Handler.cs:12"))

    def test_only_constraint_identifiers_already_in_source_can_be_returned(self):
        output = failure_diagnostics(self.result(message=(
            'Npgsql.PostgresException: 23514: violates constraint "ck_demo_integrity"; '
            'constraint "ck_private_dynamic_value"; constraint "Password=synthetic-secret"')), self.root)
        self.assertIn("constraint=ck_demo_integrity", output)
        self.assertNotIn("ck_private_dynamic_value", " ".join(output))
        self.assertNotIn("synthetic-secret", " ".join(output))


if __name__ == "__main__":
    unittest.main()
