using Community.PowerToys.Run.Plugin.TemplateRunner.Util;
using ManagedCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Wox.Infrastructure.Storage;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.TemplateRunner {

    /// <summary>
    /// Main class of this plugin that implement all used interfaces.
    /// </summary>
    public class Main : IPlugin, IContextMenu, IDisposable, ISavable {

        /// <summary>
        /// Saved Plugin context for utils
        /// </summary>
        private PluginInitContext Context { get; set; }

        /// <summary>
        /// Path to the icon of the right theme
        /// </summary>
        private readonly IconLoader IconLoader = new();

        /// <summary>
        /// API for saving and loading the Storage variable
        /// </summary>
        private readonly PluginJsonStorage<Storage> StorageApi = new();

        /// <summary>
        /// Storage that is persisted through sessions using the StorageApi
        /// </summary>
        private Storage Storage = new();

        /// <summary>
        /// Result of the last template run with a return value to be displayed
        /// </summary>
        private RunResult LastQueryRunResult = null;


        #region IDisposable

        private bool Disposed { get; set; }

        /// <summary>
        /// Initialize the plugin with the given <see cref="PluginInitContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="PluginInitContext"/> for this plugin.</param>
        public void Init(PluginInitContext context) {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateTheme(Context.API.GetCurrentTheme());

            this.Storage = this.StorageApi.Load();
        }

        /// <inheritdoc/>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Wrapper method for <see cref="Dispose()"/> that dispose additional objects and events form the plugin itself.
        /// </summary>
        /// <param name="disposing">Indicate that the plugin is disposed.</param>
        protected virtual void Dispose(bool disposing) {
            if (Disposed || !disposing) {
                return;
            }

            if (Context?.API != null) {
                Context.API.ThemeChanged -= OnThemeChanged;
            }

            Disposed = true;
        }

        private void UpdateTheme(Theme theme) => this.IconLoader.ThemeName = theme == Theme.Light || theme == Theme.HighContrastWhite ? "light" : "dark";

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateTheme(newTheme);

        #endregion

        #region ISavable

        /// <summary>
        /// Saves any unsaved storage state
        /// </summary>
        public void Save() {
            this.StorageApi.Save();
        }

        #endregion

        #region IContextMenu

        /// <summary>
        /// Return a list context menu entries for a given <see cref="Result"/> (shown at the right side of the result).
        /// </summary>
        /// <param name="selectedResult">The <see cref="Result"/> for the list with context menu entries.</param>
        /// <returns>A list context menu entries.</returns>
        public List<ContextMenuResult> LoadContextMenus(Result selectedResult) {
            // return selectedResult.ContextData is IEnumerable<ContextMenuResult> runResultContextItems
            //     ? runResultContextItems.ToList()
            //     : [];
            if (selectedResult.ContextData is IEnumerable<ContextMenuResult> runResultContextItems) {
                return runResultContextItems.ToList();
            }

            return [];
        }

        #endregion

        #region IPlugin

        /// <summary>
        /// ID of the plugin.
        /// This joke is not documented, but is required for the plugin to work
        /// </summary>
        public static string PluginID => "2CFA7264031F465E953DC55207706256";

        /// <summary>
        /// Name of the plugin.
        /// </summary>
        public string Name => "TemplateRunner";

        /// <summary>
        /// Description of the plugin.
        /// </summary>
        public string Description => "A mini command manager with parameterization capabilities";

        /// <summary>
        /// Main Plugin interface for generating items on the UI
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public List<Result> Query(Query query) {
            try {
                if (query.Terms.ElementAtOrDefault(0) == Menu.RUN) {
                    return this.Run(query);
                }

                if (query.Terms.ElementAtOrDefault(0) == Menu.ADD) {
                    return this.Add(query);
                }

                if (query.Terms.ElementAtOrDefault(0) == Menu.HISTORY) {
                    return this.History(query);
                }

                var navigationMenus = PluginUtil.NavigationMenu(this.Context, query, [], [
                    new Result {
                        QueryTextDisplay = Menu.RUN,
                        IcoPath = this.IconLoader.MAIN,
                        Title = Menu.RUN,
                        SubTitle = "Run a template",
                    },
                    new Result {
                        QueryTextDisplay = Menu.ADD,
                        IcoPath = this.IconLoader.MAIN,
                        Title = Menu.ADD,
                        SubTitle = "Add a new template",
                    },
                    new Result {
                        QueryTextDisplay = Menu.HISTORY,
                        IcoPath = this.IconLoader.MAIN,
                        Title = Menu.HISTORY,
                        SubTitle = "See the history of template runs",
                    },
                ]);

                return navigationMenus.Count > 0 ? navigationMenus : [
                    new Result {
                        QueryTextDisplay = "",
                        IcoPath = this.IconLoader.WARNING,
                        Title = "No result",
                        SubTitle = "",
                        Action = actionContext => false,
                    }
                ];
            } catch (Exception ex) {
                return [
                    new Result {
                        QueryTextDisplay = "",
                        IcoPath = this.IconLoader.WARNING,
                        Title = "Error",
                        SubTitle = $"{ex.Message}\n{ex.StackTrace}",
                        Action = actionContext => false,
                    },
                ];
            }
        }

        /// <summary>
        /// Creates executable templates
        /// 
        /// Example user input (excluding parent menus): See the Template type
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private List<Result> Add(Query query) {
            string menuRawUserQuery = PluginUtil.TrimMenuFromRawUserQuery(query, [Menu.ADD]);

            // Validate alias creation query
            Template template = new(menuRawUserQuery);

            bool aliasExists = this.Storage.TemplateDefinitions.Any((templateDefinition) => new Template(templateDefinition).Alias == template.Alias);
            return template.IsWellDefined()
            ? [
                new Result {
                    QueryTextDisplay = "",
                    IcoPath = this.IconLoader.MAIN,
                    Title = "Add Template" + (aliasExists ? " (WARNING: Alias exists, it will be overwritten)" : ""),
                    SubTitle = template.Info(),
                    Action = actionContext => {
                        this.Storage.TemplateDefinitions.RemoveAll((templateDefinition) => new Template(templateDefinition).Alias == template.Alias);
                        this.Storage.TemplateDefinitions.Add(menuRawUserQuery);
                        this.Save();

                        this.Context.API.ChangeQuery($"{query.ActionKeyword} {Menu.RUN} {template.Alias}", true);
                        return false;
                    },
                },
            ]
            : [
                new Result {
                    QueryTextDisplay = "",
                    IcoPath = this.IconLoader.WARNING,
                    Title = "The template is not fully defined yet",
                    SubTitle = template.Info(),
                    Action = actionContext => false,
                },
            ];

        }

        /// <summary>
        /// Runs templates
        /// 
        /// Example user input (excluding parent menus): See the Template type
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private List<Result> Run(Query query) {
            string menuRawUserQuery = PluginUtil.TrimMenuFromRawUserQuery(query, [Menu.RUN]);

            // Create alias-template map for convenience
            Dictionary<string, Template> templateMap = [];
            foreach (var templateDefinition in this.Storage.TemplateDefinitions) {
                Template template = new(templateDefinition);
                templateMap.Add(template.Alias, template);
            }

            TemplateRun templateRun = new(menuRawUserQuery);
            if (templateMap.ContainsKey(templateRun.Alias)) {
                // Specific alias found, provide runner result
                Template template = templateMap[templateRun.Alias];

                List<Result> results = templateRun.IsWellDefined(template)
                ? [
                    new Result {
                        QueryTextDisplay = PluginUtil.TrimMenuFromRawUserQuery(query, []),
                        IcoPath = this.IconLoader.MAIN,
                        Title = $"Run {template.Alias}",
                        SubTitle = templateRun.TemplateMatchInfo(template),
                        Action = actionContext => {
                            this.AddItemToHistory(menuRawUserQuery);

                            this.LastQueryRunResult = templateRun.Run(template);

                            if (this.LastQueryRunResult == null) {
                                return true;
                            }

                            this.Context.API.ChangeQuery(query.RawUserQuery, true);
                            return false;
                        },
                        ContextData = new ContextMenuResult[]{
                            getEditTemplateContextMenuResult(query.ActionKeyword, template.Alias),
                            getDeleteTemplateContextMenuResult(query.ActionKeyword, template.Alias),
                        },
                    }
                ]
                : [
                    new Result {
                        QueryTextDisplay = PluginUtil.TrimMenuFromRawUserQuery(query, []),
                        IcoPath = this.IconLoader.WARNING,
                        Title = $"The run definition is incorrect",
                        SubTitle = templateRun.TemplateMatchInfo(template),
                        ContextData = new ContextMenuResult[]{
                            getEditTemplateContextMenuResult(query.ActionKeyword, template.Alias),
                            getDeleteTemplateContextMenuResult(query.ActionKeyword, template.Alias),
                        },
                    }
                ];

                if (this.LastQueryRunResult == null) {
                    return results;
                }
                var lastQueryRunResult = this.LastQueryRunResult;
                this.LastQueryRunResult = null;

                int exitCode = lastQueryRunResult.ExitCode;
                string output = lastQueryRunResult.Output;
                results.Add(lastQueryRunResult.Finished
                    ? new Result {
                        QueryTextDisplay = PluginUtil.TrimMenuFromRawUserQuery(query, []),
                        IcoPath = lastQueryRunResult.ExitCode == 0 ? this.IconLoader.MAIN : this.IconLoader.WARNING,
                        Title = $"Process Result (Exit code: {lastQueryRunResult.ExitCode})",
                        SubTitle = lastQueryRunResult.Output,
                        ContextData = new ContextMenuResult[]{
                            new() {
                                PluginName = this.Name,
                                Title = $"Copy Return Code (Ctrl+R)",
                                Glyph = "\xE8C8", // Copy
                                FontFamily = "Consolas, \"Courier New\", monospace",
                                AcceleratorModifiers = ModifierKeys.Control,
                                AcceleratorKey = Key.R,
                                Action = actionContext => {
                                    Clipboard.SetDataObject(exitCode.ToString());
                                    return false;
                                }
                            },
                            new() {
                                PluginName = this.Name,
                                Title = $"Copy Output (Ctrl+O)",
                                Glyph = "\xE8C8", // Copy
                                FontFamily = "Consolas, \"Courier New\", monospace",
                                AcceleratorModifiers = ModifierKeys.Control,
                                AcceleratorKey = Key.O,
                                Action = actionContext => {
                                    Clipboard.SetDataObject(output);
                                    return false;
                                }
                            },
                        },
                    }
                    : new Result {
                        QueryTextDisplay = PluginUtil.TrimMenuFromRawUserQuery(query, []),
                        IcoPath = this.IconLoader.WARNING,
                        Title = $"The process did not finish in time => got terminated",
                        SubTitle = lastQueryRunResult.Output,
                        ContextData = new ContextMenuResult[]{
                            new() {
                                PluginName = this.Name,
                                Title = $"Copy Output (Ctrl+O)",
                                Glyph = "\xE8C8", // Copy
                                FontFamily = "Consolas, \"Courier New\", monospace",
                                AcceleratorModifiers = ModifierKeys.Control,
                                AcceleratorKey = Key.O,
                                Action = actionContext => {
                                    Clipboard.SetDataObject(output);
                                    return false;
                                }
                            },
                        },
                    }
                );

                return results;
            }

            // No specific alias was found => provide a filtered list
            var navigationMenus = PluginUtil.NavigationMenu(this.Context, query, [Menu.RUN], this.Storage.TemplateDefinitions.Select(templateDefinition => {
                Template template = new(templateDefinition);
                return new Result {
                    QueryTextDisplay = template.Alias,
                    IcoPath = this.IconLoader.MAIN,
                    Title = template.Alias,
                    SubTitle = templateDefinition,
                    ContextData = new ContextMenuResult[]{
                        getEditTemplateContextMenuResult(query.ActionKeyword, template.Alias),
                        getDeleteTemplateContextMenuResult(query.ActionKeyword, template.Alias),
                    },
                };
            }).ToList(), "");

            return navigationMenus.Count > 0 ? navigationMenus : [
                new Result {
                    QueryTextDisplay = "",
                    IcoPath = this.IconLoader.WARNING,
                    Title = "There are no aliases containing the search term",
                    SubTitle = "",
                    Action = actionContext => false,
                }
            ];

        }

        /// <summary>
        /// Shows rn history
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private List<Result> History(Query query) {
            string menuRawUserQuery = PluginUtil.TrimMenuFromRawUserQuery(query, [Menu.HISTORY]);

            int index = 0;
            List<Result> historyResults = this.Storage.History
                .Where(templateRunDefinition => templateRunDefinition.Contains(menuRawUserQuery))
                .Select(templateRunDefinition => new Result {
                    QueryTextDisplay = query.Search,
                    IcoPath = this.IconLoader.MAIN,
                    Title = $"{index++}: {templateRunDefinition}",
                    Action = actionContext => {
                        this.Context.API.ChangeQuery($"{query.ActionKeyword} {Menu.RUN} {templateRunDefinition}", true);
                        return false;
                    },
                }).ToList();

            return historyResults.Count > 0 ? PluginUtil.FixPositionAsScore(historyResults).ToList() : [
                new Result {
                    QueryTextDisplay = query.Search,
                    IcoPath = this.IconLoader.WARNING,
                    Title = "No history result",
                }
            ];
        }


        /// <summary>
        /// Adds the item to the beginning of the history, and removes any earlier occurrence
        /// This means that every element in the history appears only once, as older elements effectively move up when they reappear
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private void AddItemToHistory(string templateRunDefinition) {
            if (this.Storage.History.First?.Value == templateRunDefinition) {
                // There is no point in removing and re-adding the first element
                return;
            }

            this.Storage.History.Remove(templateRunDefinition);
            this.Storage.History.AddFirst(templateRunDefinition);
            while (this.Storage.History.Count > 100) {
                // There should never be more than 1 item excess in normal usage, but this implicitly fixes the size on a max size change
                this.Storage.History.RemoveLast();
            }

            // Saving here is done to avoid loss when the process is killed
            this.StorageApi.Save();
        }

        /// <summary>
        /// Creates a context menu object for editing a template
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private ContextMenuResult getEditTemplateContextMenuResult(string actionKeyword, string templateAlias) {
            // Find must always find the definition, because the edit context is only supposed to be added to items related to existing templates
            string templateDefinition = this.Storage.TemplateDefinitions.Find((templateDefinition) => new Template(templateDefinition).Alias == templateAlias);
            return new ContextMenuResult {
                PluginName = this.Name,
                Title = "Edit Template (Ctrl+E)",
                Glyph = "\U0001F589", // Pencil
                FontFamily = "Consolas, \"Courier New\", monospace",
                AcceleratorModifiers = ModifierKeys.Control,
                AcceleratorKey = Key.E,
                Action = actionContext => {
                    this.Context.API.ChangeQuery($"{actionKeyword} {Menu.ADD} {templateDefinition}", true);
                    return false;
                }
            };
        }

        /// <summary>
        /// Creates a context menu object for deleting a template
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        private ContextMenuResult getDeleteTemplateContextMenuResult(string actionKeyword, string templateAlias) {
            return new ContextMenuResult {
                PluginName = this.Name,
                Title = "Delete Template (Ctrl+D)",
                Glyph = "\U0001F5D1", // Trash
                FontFamily = "Consolas, \"Courier New\", monospace",
                AcceleratorModifiers = ModifierKeys.Control,
                AcceleratorKey = Key.D,
                Action = actionContext => {
                    this.Storage.TemplateDefinitions.RemoveAll((templateDefinition) => new Template(templateDefinition).Alias == templateAlias);
                    this.Context.API.ChangeQuery($"{actionKeyword} {Menu.RUN}", true);
                    return false;
                }
            };
        }

        #endregion
    }
}
