/**
 * Types mirroring the host's contracts.
 *
 * These correspond one-to-one with types in `Cayrast.Abstractions` and
 * `Cayrast.Core.Settings`. Keep them in step: the bridge serialises with camelCase
 * naming and string enums, so a divergence here surfaces as `undefined` at runtime
 * rather than as a build error.
 */

export type ThemeMode = 'System' | 'Light' | 'Dark' | 'Custom';

export type DockPosition = 'Center' | 'Top' | 'Bottom' | 'Left' | 'Right' | 'Custom';

export interface AppearanceSettings {
  theme: ThemeMode;
  customThemeId: string | null;
  accentColor: string;
  useSystemAccent: boolean;
  dockPosition: DockPosition;
  panelWidth: number;
  panelMaxHeight: number;
  borderRadius: number;
  transparency: number;
  blurStrength: number;
  shadowIntensity: number;
  animationSpeed: number;
  respectReducedMotion: boolean;
  fontFamily: string;
  uiScale: number;
}

export interface BehaviorSettings {
  hotkey: string;
  launchAtStartup: boolean;
  hideOnFocusLoss: boolean;
  clearQueryOnHide: boolean;
  showOnActiveMonitor: boolean;
  showTrayIcon: boolean;
}

export interface SearchSettings {
  enabledCategories: string[];
  maxResults: number;
  debounceMilliseconds: number;
  indexedFolders: string[];
}

export interface PrivacySettings {
  enableBrowserHistory: boolean;
  enableClipboardHistory: boolean;
  encryptClipboard: boolean;
  respectClipboardExclusions: boolean;
}

export interface UpdateSettings {
  checkAutomatically: boolean;
  includePrerelease: boolean;
  automaticallyInstall: boolean;
}

export interface CayrastSettings {
  schemaVersion: number;
  appearance: AppearanceSettings;
  behavior: BehaviorSettings;
  search: SearchSettings;
  privacy: PrivacySettings;
  updates: UpdateSettings;
}

/** How a result's icon should be resolved. */
export type IconKind = 'None' | 'Glyph' | 'ExtractedFromFile' | 'ModuleAsset' | 'DataUri';

export interface IconReference {
  kind: IconKind;
  value: string | null;
}

export interface ResultAction {
  id: string;
  title: string;
  shortcut: string | null;
  isDestructive: boolean;
}

export interface SearchCategory {
  id: string;
  displayName: string;
  sortOrder: number;
}

export interface SearchResult {
  id: string;
  title: string;
  subtitle: string | null;
  category: SearchCategory;
  icon: IconReference;
  score: number;

  /**
   * Character positions in `title` that matched the query.
   *
   * Supplied by the provider that produced the result, because only it knows how
   * the match was made. Showing the user *why* something matched is most of what
   * separates fuzzy search that feels intelligent from fuzzy search that feels random.
   */
  titleMatchIndices: number[];

  actions: ResultAction[];
}

export interface SearchResponse {
  results: SearchResult[];
}

export interface AppInfo {
  product: string;
  version: string;
  os: string;
}

/** The control type used to edit a setting. */
export type SettingKind =
  | 'Boolean'
  | 'Text'
  | 'Integer'
  | 'Slider'
  | 'Choice'
  | 'Color'
  | 'Hotkey'
  | 'Path';

/**
 * One setting, as declared by the host.
 *
 * The settings screen is generated from these rather than hand-built, which is the
 * only reason settings search can exist: the screen and the search index are the same
 * data, so they cannot drift apart.
 */
export interface SettingDescriptor {
  id: string;
  category: string;
  label: string;
  description: string | null;
  kind: SettingKind;
  defaultValue: unknown;

  /**
   * Extra terms that should match this setting.
   *
   * What lets someone find the transparency slider by typing "glass" or "acrylic"
   * rather than having to guess the label a developer chose.
   */
  keywords: string[];
  choices: { value: string; label: string }[];
  minimum: number | null;
  maximum: number | null;
  ownerModuleId: string | null;
  requiresRestart: boolean;
}

/** An installed module, as reported by the host. */
export interface ModuleInfo {
  id: string;
  name: string;
  version: string;
  author: string;
  description: string;
  permissions: string;
  trustLevel: string;
  state: string;
  failureReason: string | null;
}
