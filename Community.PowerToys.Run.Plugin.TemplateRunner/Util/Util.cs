using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

namespace Community.PowerToys.Run.Plugin.TemplateRunner.Util {

    /// <summary>
    /// Represents the persistent storage of the plugin
    /// </summary>
    class Storage {
        /// <summary>
        /// Favorites can be added from the history
        /// By default new ones are added to the end
        /// The order can be adjusted
        /// </summary>
        public List<string> TemplateDefinitions { get; set; } = [];

        /// <summary>
        /// The latest item is the first
        /// The history has no repetition, as re-picking would cause a spamming of the history
        /// => the older occurrence of the item is removed, and only the latest is kept
        /// </summary>
        public LinkedList<string> History { get; set; } = [];
    }

    /// <summary>
    /// Top level plugin menu keys
    /// </summary>
    static class Menu {
        public static string RUN = "run";
        public static string ADD = "add";
        public static string HISTORY = "history";
    }

    /// <summary>
    /// Result of an executed process
    /// </summary>
    class RunResult {
        public bool Finished { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
    }

    /// <summary>
    /// Executable info
    /// </summary>
    class ExecutionInfo {
        public string WorkingDirectory { get; set; }
        public string Executable { get; set; }
        public string[] Arguments { get; set; }
        /// <summary>Unit: >=0 ms, -1 ~ infinite, anything else is an invalid value</summary>
        public int Timeout { get; set; }
    }

    /// <summary>
    /// An enum-like thingy for defining the execution type of a template
    /// </summary>
    public class TemplateMode {
        #region values
        public static TemplateMode Launch { get { return new TemplateMode("launch"); } }
        public static TemplateMode Return { get { return new TemplateMode("return"); } }
        public static TemplateMode Uri { get { return new TemplateMode("uri"); } }

        /// <summary>List of "enum" values for reverse lookup</summary>
        public static readonly TemplateMode[] AllModes = [
            TemplateMode.Launch,
            TemplateMode.Return,
            TemplateMode.Uri,
        ];
        #endregion

        #region instance
        private TemplateMode(string value) { Value = value; }

        /// <summary>Value associated with the "enum"</summary>
        private string Value { get; set; }
        #endregion

        #region conversion
        /// <summary>Get the "enum" associated with the provided value</summary>
        /// <param name="value">Value associated with the "enum" (case insensitive)</param>
        /// <returns>The related "enum" if there is one, otherwise null</returns>
        public static TemplateMode FromString(string value) {
            return TemplateMode.AllModes.FirstOrDefault((mode) => mode.ToString().Equals(value, StringComparison.CurrentCultureIgnoreCase), null);
        }

        /// <summary> String value of the "enum" </summary>
        /// <returns></returns>
        public override string ToString() {
            return this.Value;
        }
        #endregion

        #region comparators
        public override int GetHashCode() {
            return this.Value.GetHashCode();
        }

        public override bool Equals(object obj) {
            if (obj is TemplateMode templateMode) {
                return this.Value == templateMode?.ToString();
            }
            return false;
        }

        public static bool operator ==(TemplateMode obj1, TemplateMode obj2) {
            return (ReferenceEquals(obj1, null) && ReferenceEquals(obj2, null)) || (obj1?.Equals(obj2) ?? false);
        }

        public static bool operator !=(TemplateMode obj1, TemplateMode obj2) {
            return !(obj1 == obj2);
        }
        #endregion
    }

    /// <summary>
    /// Parameterizable command template
    /// </summary>
    class Template {
        /// <summary>
        /// The original string describing the template
        /// </summary>
        public string Definition { get; set; }

        /// <summary>
        /// Identifier of the template, also used to call it
        /// </summary>
        public string Alias { get; set; }

        /// <summary>
        /// Character used as a separator in the definition
        /// It is the first non-word (Regex) character in the definition
        /// </summary>
        public string Separator { get; set; }

        /// <summary>
        /// Describes how the template
        /// </summary>
        public TemplateMode Mode { get; set; }

        /// <summary>
        /// Template parameters to be replaced runtime with a raw string replace
        /// </summary>
        public string[] Parameters { get; set; }
        public ExecutionInfo ExecutionInfo { get; set; }

        /// <summary>
        /// Initializes the object based on the definition
        /// It's supposed to never result in an exception, as the created object is meant to be used for preview purposes
        /// </summary>
        /// <param name="templateDefinition">
        /// Format (sep ~ separator):
        /// <alias><sep><mode><sep>[<timeoutMs><sep>][<parameter><sep>[...]]<sep><sep><workingDir><sep><executable><sep>[<argument><sep>[...]]
        /// The timeout is mandatory for return mode, and not definable otherwise
        /// </param>
        public Template(string templateDefinition) {
            this.Definition = templateDefinition;

            var separatorMatch = Regex.Match(templateDefinition, @"[^\w]");
            this.Separator = separatorMatch.Success ? separatorMatch.Value : " ";

            var interfaceSegments = templateDefinition.Split($"{this.Separator}{this.Separator}", 2);
            string aliasInterface = interfaceSegments.Length > 0 ? interfaceSegments[0] : "";
            var commandInterface = interfaceSegments.Length > 1 ? interfaceSegments[1] : "";

            var aliasInterfaceSegments = aliasInterface.Split(this.Separator);
            // Only whitespaces are not valid aliases
            this.Alias = aliasInterfaceSegments.Length > 0 && !string.IsNullOrWhiteSpace(aliasInterfaceSegments[0]) ? aliasInterfaceSegments[0] : null;
            this.Mode = aliasInterfaceSegments.Length > 1 ? TemplateMode.FromString(aliasInterfaceSegments[1]) : null;

            int parametersStartIndex = 2;
            int timeout = -1;
            if (this.Mode == TemplateMode.Return) {
                parametersStartIndex = 3;

                // The second argument is optional, but if it's there, it must be the timeout
                if (aliasInterfaceSegments.Length > 2) {
                    bool parseSuccessful = Int32.TryParse(aliasInterfaceSegments[2], out timeout);
                    timeout = parseSuccessful ? timeout : -2; // -2 indicates an invalid value
                }
            }
            this.Parameters = aliasInterfaceSegments.Length > parametersStartIndex ? aliasInterfaceSegments[parametersStartIndex..] : [];

            // Note: The empty string is not a valid path
            if (this.Mode == TemplateMode.Uri) {
                // URIs have no working directory, and their parameters are included in themselves
                this.ExecutionInfo = new() {
                    WorkingDirectory = null,
                    Executable = commandInterface != "" ? commandInterface : null,
                    Arguments = [],
                    Timeout = timeout,
                };
            } else { // Normal process stuff
                var commandInterfaceSegments = commandInterface.Split(this.Separator);
                this.ExecutionInfo = new() {
                    WorkingDirectory = commandInterfaceSegments.Length > 0 && commandInterfaceSegments[0] != "" ? commandInterfaceSegments[0] : null,
                    Executable = commandInterfaceSegments.Length > 1 && commandInterfaceSegments[1] != "" ? commandInterfaceSegments[1] : null,
                    Arguments = commandInterfaceSegments.Length > 2 ? commandInterfaceSegments[2..] : [],
                    Timeout = timeout,
                };
            }
        }

        /// <summary>
        /// Tells whether the template represented is sufficiently defined to function
        /// </summary>
        /// <returns></returns>
        public bool IsWellDefined() {
            return this.Alias != null &&
                   this.Mode != null &&
                   this.ExecutionInfo.Timeout >= -1 &&
                   this.ExecutionInfo.Executable != null &&
                   // It's not a valid URI without a ':'
                   (this.Mode != TemplateMode.Uri || this.ExecutionInfo.Executable.Contains(':'));
        }

        /// <summary>
        /// Provides an info string detailing the parsed template
        /// </summary>
        /// <returns></returns>
        public string Info() {
            return string.Join("\n",
                new string[]{
                    $"Template: '{this.Definition}'",
                    "Separator: " +
                        (this.Separator == null ? "undefined" : $"'{this.Separator}'") +
                        " (first non-regex-word character)",
                    $"Mode: " +
                        (this.Mode == null ? "undefined" : $"'{this.Mode}'") +
                        $" ({string.Join("|", TemplateMode.AllModes.Select(mode => mode.ToString()))})",
                }
                .Concat(this.Mode != TemplateMode.Return ? [] : [
                    "Timeout: " + (
                        this.ExecutionInfo.Timeout >= -1
                            ? this.ExecutionInfo.Timeout.ToString()
                            : "Bad number, must be >= -1"
                    ) + " (ms, -1 ~ wait indefinitely)"]
                )
                .Concat([
                    "",
                    $"Alias: " +(this.Alias == null ? "undefined" : $"'{this.Alias}'"),
                ])
                .Concat(this.Parameters.Select((templateParameter) => $"Parameter: '{templateParameter}'"))
                .Concat(this.Mode == TemplateMode.Uri ? [] : [
                    $"Working Directory: " + (
                        string.IsNullOrEmpty(this.ExecutionInfo.WorkingDirectory)
                            ? "PowerToys working directory"
                            : $"'{this.ExecutionInfo.WorkingDirectory}'"
                    ),
                ])
                .Concat([
                    $"Executable: " +
                        (this.ExecutionInfo.Executable == null ? "undefined" : $"'{this.ExecutionInfo.Executable}'") +
                        (this.Mode == TemplateMode.Uri && this.ExecutionInfo.Executable != null && !this.ExecutionInfo.Executable.Contains(':')
                            ? " (ISSUE: URIs must contain a ':')"
                            : ""),
                ])
                .Concat(this.ExecutionInfo.Arguments.Select((argument) => $"Executable Argument: '{argument}'"))
            );
        }
    }

    /// <summary>
    /// Executes a templates with the provided parameterization
    /// </summary>
    class TemplateRun {
        /// <summary>
        /// Identifier of the template, also used to call it
        /// </summary>
        public string Alias { get; set; }

        /// <summary>
        /// Character used as a separator in the definition
        /// It is the first non-word (Regex) character in the definition
        /// </summary>
        public string Separator { get; set; }

        /// <summary>
        /// Parameters to replace in the template
        /// </summary>
        public string[] Parameters { get; set; }

        /// <summary>
        /// Initializes the object based on the definition
        /// It's supposed to never result in an exception, as the created object is meant to be used for preview purposes
        /// </summary>
        /// <param name="templateRunDefinition">
        /// Format (sep ~ separator):
        /// <alias><sep>[<argument><sep>[...]]
        /// </param>
        public TemplateRun(string templateRunDefinition) {
            var separatorMatch = Regex.Match(templateRunDefinition, @"[^\w]");
            this.Separator = separatorMatch.Success ? separatorMatch.Value : " ";

            var interfaceSegments = templateRunDefinition.Split(this.Separator);
            this.Alias = interfaceSegments.Length > 0 ? interfaceSegments[0] : null;
            this.Parameters = interfaceSegments.Length > 1 ? interfaceSegments[1..] : [];
        }

        /// <summary>
        /// Tells whether the template run represented is sufficiently defined for a given template
        /// </summary>
        /// <param name="template">Reference template</param>
        /// <returns></returns>
        public bool IsWellDefined(Template template) {
            return this.Parameters.Length == template.Parameters.Length;
        }

        /// <summary>
        /// Provides an info string detailing the parsed template
        /// </summary>
        /// <returns></returns>
        public string TemplateMatchInfo(Template template) {
            // The param info is meant to work even if a different amount of parameters are provided than expected
            List<string> parameterMappingInfo = [];
            for (int i = 0; i < Math.Max(template.Parameters.Length, this.Parameters.Length); i++) {
                string templateParamName = i < template.Parameters.Length ? $"'{template.Parameters[i]}'" : "undefined";
                string templateParamValue = i < this.Parameters.Length ? $"'{this.Parameters[i]}'" : "undefined";
                parameterMappingInfo.Add($"Template Parameter: {templateParamName} -> {templateParamValue}");
            }

            ExecutionInfo executionInfo = this.ResolveTemplate(template);
            return string.Join("\n",
                new string[]{
                    $"Alias: '{this.Alias}'",
                    $"Run Separator: '{this.Separator}'",
                    $"Template: '{template.Definition}'",
                    $"Template Mode: '{template.Mode}'",
                    "",
                }
                .Concat(parameterMappingInfo)
                .Concat([
                    "",
                    $"Working Directory: {executionInfo.WorkingDirectory}",
                    $"Executable: {executionInfo.Executable}",
                ])
                .Concat(executionInfo.Arguments.Select(argument => $"Executable Argument: '{argument}'"))
            );
        }

        /// <summary>
        /// Combines the template and the run parameterization to execution information that can be used to run a process
        /// This will work even if an inappropriate amount of parameters are defined for the given template, but the result
        /// will most likely not be what the user intended (this is for previewing the "thing" to be executed)
        /// </summary>
        /// <param name="template"></param>
        /// <returns></returns>
        public ExecutionInfo ResolveTemplate(Template template) {
            // For URIs, the template parameter values are URI encoded
            var runParameters = template.Mode == TemplateMode.Uri ? this.Parameters.Select(Uri.EscapeDataString).ToArray() : this.Parameters;

            return new ExecutionInfo() {
                WorkingDirectory = string.IsNullOrEmpty(template.ExecutionInfo.WorkingDirectory)
                    ? Directory.GetCurrentDirectory()
                    : TemplateRun.ResolveTemplateString(template.ExecutionInfo.WorkingDirectory, template.Parameters, runParameters),
                Executable = TemplateRun.ResolveTemplateString(template.ExecutionInfo.Executable, template.Parameters, runParameters),
                Arguments = template.ExecutionInfo.Arguments
                    .Select(argument => TemplateRun.ResolveTemplateString(argument, template.Parameters, runParameters))
                    .ToArray(),
                Timeout = template.ExecutionInfo.Timeout,
            };
        }

        /// <summary>
        /// Replace a set of string in a source string
        /// All replacements happen in the original source string, meaning that one part of the source
        /// cannot be replaced after it was replaced once, to avoid confusion
        /// In case there is overlap, the first, and the if there is no first, the longer match will be applied
        /// </summary>
        /// <param name="source">source to perform the replacements on</param>
        /// <param name="originals">strings to replace in the source string</param>
        /// <param name="replacements">strings to replace with (each only replaces the string to be replaced with the same index)</param>
        /// <example>
        /// ResolveTemplateString("123", ["1", "3"], ["x", "y"]) // "x2y"
        /// ResolveTemplateString("123", ["4"], ["x"]) // "123"
        /// ResolveTemplateString("123", ["1"], ["x", "y"]) // "x23"
        /// ResolveTemplateString("123", ["1", "3"], ["x"]) // "x23"
        /// ResolveTemplateString("123", ["12", "23"], ["x", "y"]) // "x3"
        /// ResolveTemplateString("123", ["12", "123"], ["x", "y"]) // "y"
        /// </example>
        /// <returns></returns>
        private static string ResolveTemplateString(string source, string[] originals, string[] replacements) {
            int resolvedParameterCount = Math.Min(originals.Length, replacements.Length);
            if (resolvedParameterCount == 0) return source;

            string[] resolvedOriginals = originals[..resolvedParameterCount];
            string[] resolvedReplacements = replacements[..resolvedParameterCount];

            var escapedOriginals = originals.Select(Regex.Escape);
            string originalsOrPattern = string.Join("|", resolvedOriginals);
            return Regex.Replace(source, originalsOrPattern, match => {
                int replacementIndex = resolvedOriginals.ToList().IndexOf(match.Value);
                return resolvedReplacements[replacementIndex];
            });
        }

        /// <summary>
        /// Runs the provided template with this run configuration
        /// </summary>
        /// <param name="template">Reference template</param>
        /// <returns>The process execution result on return mode templates, null otherwise</returns>
        public RunResult Run(Template template) {
            if (!this.IsWellDefined(template)) {
                throw new Exception("The template run definition is not compatible with the used template");
            }

            ExecutionInfo executionInfo = this.ResolveTemplate(template);
            string output = "";

            Process process = new();
            process.StartInfo.FileName = executionInfo.Executable;

            if (template.Mode == TemplateMode.Uri) {
                // Shell is needed for URIs to be recognized as executables
                process.StartInfo.UseShellExecute = true;

                // URIs don't have a working directory, and have their parameters in themselves
            } else {
                // Generally better to avoid shell if not necessary, required for capturing output and it also helps in keeping arguments separate
                process.StartInfo.UseShellExecute = false;

                process.StartInfo.WorkingDirectory = executionInfo.WorkingDirectory;
                foreach (var argument in executionInfo.Arguments) {
                    process.StartInfo.ArgumentList.Add(argument);
                }
            }

            if (template.Mode == TemplateMode.Return) {
                // The appearing process window de-focuses PowerToys Run, and dismisses it
                // Hiding it is a smoother experience anyway
                process.StartInfo.CreateNoWindow = true;

                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                DataReceivedEventHandler outputHandler = new((sendingProcess, dataReceivedEventArgs) => { output += dataReceivedEventArgs.Data + "\n"; });
                process.OutputDataReceived += outputHandler;
                process.ErrorDataReceived += outputHandler;
            }

            process.Start();

            if (template.Mode == TemplateMode.Return) {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(executionInfo.Timeout);

                return new RunResult() {
                    Finished = process.HasExited,
                    ExitCode = process.HasExited ? process.ExitCode : 0,
                    Output = output
                };
            }

            // Other modes don't return a result
            return null;
        }
    }

}
