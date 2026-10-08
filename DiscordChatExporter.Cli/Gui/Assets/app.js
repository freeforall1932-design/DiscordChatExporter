"use strict";

const TOKEN_KEY = "discordchatexporter.token";
const REMEMBER_KEY = "discordchatexporter.remember";
const THEME_KEY = "discordchatexporter.theme";

const POLL_WAIT_MS = 3000;

const state = {
  /** @type {any} */ info: null,
  /** @type {any[]} */ commands: [],
  /** @type {any} */ selected: null,
  /** @type {Record<string, any>} */ values: {},
  /** @type {Record<string, any>} */ defaults: {},
  /** @type {Record<string, string>} */ knownIds: {},
  showAdvanced: false,
  /** @type {any} */ run: null,
  starting: false,
  cursor: 0,
  pendingText: "",
  pendingNode: null,
  /** Nodes of the most recently written lines, used to redraw progress bars in place */
  logTail: [],
  polling: false,
  token: "",
  remember: true,
};

const element = (id) => document.getElementById(id);

document.addEventListener("DOMContentLoaded", () => {
  void start();
});

async function start() {
  applyTheme(localStorage.getItem(THEME_KEY) || "dark");

  state.remember = localStorage.getItem(REMEMBER_KEY) !== "false";
  element("token-remember").checked = state.remember;
  state.token = state.remember ? localStorage.getItem(TOKEN_KEY) || "" : "";
  element("token").value = state.token;

  wireEvents();

  try {
    state.info = await api("/api/info");
  } catch (error) {
    setStatus("failed", "Server unreachable");
    showToast(`Could not reach the local server: ${error.message}`, "error");
    return;
  }

  state.commands = state.info.commands || [];

  element("brand-name").textContent = state.info.name;
  element("brand-meta").textContent = `v${state.info.version} · ${state.info.executableName}`;
  element("working-dir").textContent = state.info.workingDirectory;
  element("network-warning").classList.toggle("hidden", !state.info.isNetworkExposed);

  renderCommandList();
  selectCommand(state.commands[0]?.name);

  await attachToRunningCommand();
}

// —————————————————————————————— HTTP ——————————————————————————————

async function api(path, options) {
  const response = await fetch(path, options);
  const text = await response.text();

  let payload = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = null;
    }
  }

  if (!response.ok) {
    const message = payload?.message || text || `Request failed (${response.status})`;
    const error = new Error(message);
    error.status = response.status;
    error.details = payload?.details || [];
    throw error;
  }

  return payload;
}

function postJson(path, body) {
  return api(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

// ———————————————————————————— Command list ————————————————————————————

function renderCommandList() {
  const container = element("command-list");
  container.textContent = "";

  const groups = new Map();
  for (const command of state.commands) {
    const group = command.group || "Commands";
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push(command);
  }

  for (const [group, commands] of groups) {
    const title = document.createElement("div");
    title.className = "group-title";
    title.textContent = group;
    container.append(title);

    for (const command of commands) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "command";
      button.dataset.command = command.name;
      button.setAttribute("aria-current", String(command.name === state.selected?.name));
      button.title = `${command.description || command.title}\n\nCommand: ${command.name}`;

      const icon = iconSvg(command.icon);
      const text = document.createElement("span");
      text.className = "command-text";

      const name = document.createElement("span");
      name.className = "command-name";
      name.textContent = command.title;

      const rawName = document.createElement("span");
      rawName.className = "command-raw-name";
      rawName.textContent = command.name;

      text.append(name, rawName);
      button.append(icon, text);

      button.addEventListener("click", () => selectCommand(command.name));
      container.append(button);
    }
  }
}

function selectCommand(name) {
  const command = state.commands.find((c) => c.name === name);
  if (!command) return;

  state.selected = command;
  state.values = {};
  state.defaults = {};

  for (const option of command.options) {
    state.defaults[option.name] = getDefaultValue(option);
    state.values[option.name] = cloneValue(state.defaults[option.name]);
  }

  for (const button of document.querySelectorAll("#command-list .command")) {
    button.setAttribute("aria-current", String(button.dataset.command === name));
  }

  element("command-title").textContent = command.title;
  element("command-description").textContent = command.description || "";
  element("command-name").textContent = command.name;
  element("command-token-badge").classList.toggle("hidden", !command.requiresToken);

  renderForm();
  refreshCommandLinePreview();
  refreshRunAvailability();
}

// —————————————————————————————— Form ——————————————————————————————

function renderForm() {
  const form = element("command-form");
  form.textContent = "";

  const options = state.selected.options.filter(
    (option) => state.showAdvanced || !option.isAdvanced
  );

  if (options.length === 0) {
    const empty = document.createElement("p");
    empty.className = "muted";
    empty.textContent =
      "This command doesn't have any options. " + "Press “Run” to execute it.";
    form.append(empty);
    return;
  }

  for (const option of options) {
    form.append(createField(option));
  }
}

function createField(option) {
  const field = document.createElement("div");
  field.className = "field";
  if (option.kind === "list" || option.kind === "path" || option.kind === "filter") {
    field.classList.add("span");
  }

  const label = document.createElement("label");
  label.className = "field-label";

  const title = document.createElement("span");
  title.textContent = option.label;
  label.append(title);

  const flag = document.createElement("span");
  flag.className = "flag";
  flag.textContent = `--${option.name}`;
  label.append(flag);

  if (option.isRequired) {
    const required = document.createElement("span");
    required.className = "required";
    required.textContent = "*";
    required.title = "Required";
    label.append(required);
  }

  const control = createControl(option);
  if (control instanceof HTMLElement && control.id) {
    label.htmlFor = control.id;
  }

  field.append(label);

  if (option.kind === "bool") {
    const row = document.createElement("div");
    row.className = "switch-row";
    row.append(control);
    const text = document.createElement("span");
    text.textContent =
      option.defaultValue === "true" ? "Enabled by default" : "Disabled by default";
    text.className = "muted";
    row.append(text);
    field.append(row);
  } else {
    field.append(control);
  }

  if (option.description) {
    const hint = document.createElement("span");
    hint.className = "field-hint";
    hint.textContent = option.description;
    field.append(hint);
  }

  return field;
}

function createControl(option) {
  const id = `option-${option.name}`;

  if (option.kind === "bool") {
    const wrapper = document.createElement("label");
    wrapper.className = "check";

    const input = document.createElement("input");
    input.type = "checkbox";
    input.id = id;
    input.checked = state.values[option.name] === true;
    input.addEventListener("change", () => {
      state.values[option.name] = input.checked;
      onFormChanged();
    });

    wrapper.append(input);
    return wrapper;
  }

  if (option.kind === "select") {
    const select = document.createElement("select");
    select.id = id;

    for (const choice of option.choices || []) {
      const choiceElement = document.createElement("option");
      choiceElement.value = choice.value;
      choiceElement.textContent = choice.label;
      select.append(choiceElement);
    }

    select.value = state.values[option.name] ?? "";
    select.addEventListener("change", () => {
      state.values[option.name] = select.value;
      onFormChanged();
    });

    return select;
  }

  if (option.kind === "list") {
    return createChipsControl(option, id);
  }

  const input = document.createElement("input");
  input.id = id;
  input.type =
    option.kind === "number" ? "number" : option.kind === "password" ? "password" : "text";
  input.value = state.values[option.name] ?? "";
  if (option.placeholder) input.placeholder = option.placeholder;
  input.spellcheck = false;
  input.autocomplete = "off";

  if (option.name === "guild") {
    input.setAttribute("list", "known-ids");
  }

  input.addEventListener("input", () => {
    state.values[option.name] = input.value;
    onFormChanged();
  });

  if (option.kind === "date") {
    const wrapper = document.createElement("div");
    wrapper.className = "raw-row";

    const calendar = document.createElement("input");
    calendar.type = "date";
    calendar.className = "hidden";
    calendar.addEventListener("change", () => {
      input.value = calendar.value;
      state.values[option.name] = calendar.value;
      onFormChanged();
    });

    const button = document.createElement("button");
    button.type = "button";
    button.className = "btn ghost";
    button.title = "Pick a date";
    button.textContent = "Pick date";
    button.addEventListener("click", () => {
      if (typeof calendar.showPicker === "function") {
        calendar.showPicker();
      } else {
        calendar.classList.toggle("hidden");
        calendar.focus();
      }
    });

    wrapper.append(input, button, calendar);
    return wrapper;
  }

  return input;
}

function createChipsControl(option, id) {
  const container = document.createElement("div");
  container.className = "chips";

  const input = document.createElement("input");
  input.type = "text";
  input.id = id;
  input.placeholder = option.placeholder || "Type a value and press Enter";
  input.spellcheck = false;
  input.autocomplete = "off";

  const render = () => {
    for (const chip of container.querySelectorAll(".chip")) chip.remove();

    const placeholder = container.querySelector(".empty-chips");
    const values = state.values[option.name] || [];
    if (values.length === 0) {
      if (!placeholder) {
        const empty = document.createElement("span");
        empty.className = "empty-chips";
        empty.textContent = "no values yet";
        container.prepend(empty);
      }
      return;
    }

    placeholder?.remove();

    for (const value of values) {
      const chip = document.createElement("span");
      chip.className = "chip";
      chip.append(document.createTextNode(value));

      const remove = document.createElement("button");
      remove.type = "button";
      remove.textContent = "×";
      remove.title = "Remove";
      remove.addEventListener("click", () => {
        state.values[option.name] = (state.values[option.name] || []).filter(
          (v) => v !== value
        );
        render();
        onFormChanged();
      });

      chip.append(remove);
      container.prepend(chip);
    }
  };

  const commit = (rawText) => {
    const parts = String(rawText)
      .split(/[\s,;]+/)
      .map((v) => v.trim())
      .filter((v) => v.length > 0);

    if (parts.length === 0) return;

    const values = state.values[option.name] || [];
    for (const part of parts) {
      if (!values.includes(part)) values.push(part);
    }

    state.values[option.name] = values;
    input.value = "";
    render();
    onFormChanged();
  };

  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === "," || event.key === "Tab") {
      if (input.value.trim().length === 0) return;
      event.preventDefault();
      commit(input.value);
    } else if (event.key === "Backspace" && input.value.length === 0) {
      const values = state.values[option.name] || [];
      if (values.length > 0) {
        values.pop();
        state.values[option.name] = values;
        render();
        onFormChanged();
      }
    }
  });

  input.addEventListener("paste", (event) => {
    const text = event.clipboardData?.getData("text");
    if (text && /[\s,;]/.test(text.trim())) {
      event.preventDefault();
      commit(text);
    }
  });

  input.addEventListener("blur", () => {
    if (input.value.trim().length > 0) commit(input.value);
  });

  container.append(input);
  render();

  return container;
}

function onFormChanged() {
  refreshCommandLinePreview();
  refreshRunAvailability();
}

function refreshRunAvailability() {
  const errors = getValidationErrors();
  element("validation").textContent = errors.join(" ");

  // While the request is in flight the command also counts as busy, so that pressing the
  // button twice can't start two commands
  const isBusy = state.starting || (state.run ? state.run.state === "running" : false);
  element("run").disabled = isBusy || errors.length > 0;
  element("raw-run").disabled = isBusy;
  element("cancel").disabled = !isBusy;
}

function getValidationErrors() {
  if (!state.selected) return [];

  const errors = [];

  if (
    state.selected.requiresToken &&
    state.token.trim().length === 0 &&
    !state.info?.hasEnvironmentToken
  ) {
    errors.push("Enter a Discord token.");
  }

  for (const option of state.selected.options) {
    if (!option.isRequired) continue;

    const value = state.values[option.name];
    const isEmpty = Array.isArray(value)
      ? value.filter((v) => String(v).trim().length > 0).length === 0
      : String(value ?? "").trim().length === 0;

    if (isEmpty) errors.push(`'${option.label}' is required.`);
  }

  return errors;
}

function getDefaultValue(option) {
  if (option.kind === "bool") return option.defaultValue === "true";
  if (option.isSequence) return [];
  return option.hasDefault ? option.defaultValue : "";
}

function cloneValue(value) {
  return Array.isArray(value) ? [...value] : value;
}

// ————————————————————————— Payload & command line —————————————————————————

function collectOptions() {
  const options = {};

  for (const option of state.selected.options) {
    const value = state.values[option.name];
    const defaultValue = state.defaults[option.name];

    if (option.kind === "bool") {
      if (value !== defaultValue) options[option.name] = [value ? "true" : "false"];
      continue;
    }

    if (option.isSequence) {
      const list = (value || []).map((v) => String(v).trim()).filter((v) => v.length > 0);
      if (list.length > 0) options[option.name] = list;
      continue;
    }

    const text = String(value ?? "").trim();
    if (text.length > 0 && text !== String(defaultValue ?? "").trim()) {
      options[option.name] = [text];
    }
  }

  return options;
}

function buildCommandLine(command, options, token) {
  const arguments_ = [command.name];

  if (token) arguments_.push("--token", "***");

  for (const option of command.options) {
    const values = options[option.name];
    if (!values) continue;

    for (const value of option.isSequence ? values : [values[0]]) {
      arguments_.push(`--${option.name}`, String(value));
    }
  }

  return [state.info.executableName, ...arguments_.map(quoteArgument)].join(" ");
}

function quoteArgument(value) {
  return /[\s"]/.test(value) ? `"${value.replace(/"/g, '\\"')}"` : value;
}

function refreshCommandLinePreview() {
  if (!state.selected || !state.info) return;
  element("cmdline-preview").textContent = buildCommandLine(
    state.selected,
    collectOptions(),
    state.token.trim()
  );
}

// ————————————————————————————— Running —————————————————————————————

async function runSelectedCommand() {
  if (!state.selected) return;
  if (getValidationErrors().length > 0) return;

  await startRun({
    command: state.selected.name,
    options: collectOptions(),
    token: state.token,
  });
}

async function runRawCommand() {
  const line = element("raw-input").value.trim();
  if (line.length === 0) {
    showToast("Enter a command line first, for example: export --help", "warning");
    return;
  }

  await startRun({ rawCommandLine: line, token: state.token });
}

async function startRun(request) {
  if (state.starting || state.run?.state === "running") {
    showToast(
      `'${state.run?.command ?? "Another command"}' is still running. Cancel it before starting another command.`,
      "warning"
    );
    return;
  }

  state.starting = true;
  refreshRunAvailability();

  try {
    const run = await postJson("/api/runs", request);
    element("raw-input").value = request.rawCommandLine ? "" : element("raw-input").value;
    beginRun(run);
    await pollRun();
  } catch (error) {
    if (error.status === 409) {
      showToast(error.message, "warning");
      await attachToRunningCommand();
      return;
    }

    const details = (error.details || []).join(" ");
    showToast(`${error.message}${details ? ` ${details}` : ""}`, "error");
  } finally {
    state.starting = false;
    refreshRunAvailability();
  }
}

async function cancelRun() {
  if (!state.run || state.run.state !== "running") return;

  try {
    await api(`/api/runs/${state.run.id}/cancel`, { method: "POST" });
    showToast("Cancellation requested — waiting for the command to stop…", "warning");
  } catch (error) {
    showToast(`Failed to cancel: ${error.message}`, "error");
  }
}

function beginRun(run) {
  state.run = run;
  state.cursor = run.cursor || 0;

  setLog("");
  appendLog(
    `$ ${run.commandLine}\n\n`,
    "log-system"
  );

  updateRunState(run, { reset: true });
}

async function pollRun() {
  if (state.polling) return;
  state.polling = true;

  try {
    while (state.run && state.run.state === "running") {
      const snapshot = await api(
        `/api/runs/${state.run.id}?cursor=${state.cursor}&wait=${POLL_WAIT_MS}`
      );

      if (snapshot.output) appendLog(snapshot.output);

      state.cursor = snapshot.cursor;
      state.run = snapshot;

      updateRunState(snapshot);
      collectKnownIds(snapshot.output, snapshot.command);
    }
  } catch (error) {
    showToast(`Lost connection to the local server: ${error.message}`, "error");
    setStatus("failed", "Disconnected");
  } finally {
    state.polling = false;
    refreshRunAvailability();
  }
}

async function attachToRunningCommand() {
  try {
    const current = await api("/api/runs/current");
    if (!current?.run) {
      refreshRunAvailability();
      return;
    }

    if (current.run.state === "running") {
      showToast(`Reattached to the running command '${current.run.command}'.`, "warning");
      beginRun(current.run);
      await pollRun();
    }
  } catch {
    // Not critical
  }
}

function updateRunState(run, options = {}) {
  if (options.reset) {
    element("console-title").textContent = run.command;
  }

  const isRunning = run.state === "running";
  const badge = element("run-badge");

  if (isRunning) {
    setStatus("running", `Running ${run.command}…`);
    badge.classList.add("hidden");
  } else {
    const exitCode = run.exitCode ?? 0;
    badge.classList.remove("hidden");
    badge.textContent = `exit code ${exitCode}`;

    if (run.state === "succeeded") {
      setStatus("succeeded", `Finished ${run.command}`);
      showToast(`'${run.command}' finished successfully.`, "success");
    } else if (run.state === "cancelled") {
      setStatus("cancelled", `Cancelled ${run.command}`);
      showToast(`'${run.command}' was cancelled.`, "warning");
    } else {
      setStatus("failed", `Failed ${run.command}`);
      showToast(`'${run.command}' failed with exit code ${exitCode}.`, "error");
    }
  }

  const progressWrap = element("progress-wrap");
  if (run.progress !== null && run.progress !== undefined) {
    progressWrap.classList.remove("hidden");
    element("progress-bar").style.width = `${run.progress}%`;
    element("progress-label").textContent = `${run.progress}%`;
  } else {
    progressWrap.classList.add("hidden");
    element("progress-bar").style.width = "0%";
    element("progress-label").textContent = "0%";
  }

  element("log-download").disabled = false;
  refreshRunAvailability();

  // The command line is authoritative here, so it's also used for the copy button
  element("cmdline-preview").dataset.actual = run.commandLine || "";
}

/** Extracts "id | name" pairs from the output of listing commands. */
function collectKnownIds(output, command) {
  if (!output) return;

  for (const line of output.split("\n")) {
    const match = /^\s*\*?\s*(\d{5,})\s*\|\s*(.+?)\s*$/.exec(line);
    if (!match) continue;

    const [, id, name] = match;
    if (!state.knownIds[id]) {
      state.knownIds[id] = name;
      addKnownIdToList(id, name);
    }
  }
}

function addKnownIdToList(id, name) {
  let list = document.getElementById("known-ids");
  if (!list) {
    list = document.createElement("datalist");
    list.id = "known-ids";
    document.body.append(list);
  }

  if (list.querySelector(`option[value="${id}"]`)) return;

  const option = document.createElement("option");
  option.value = id;
  option.label = name;
  list.append(option);
}

// —————————————————————————————— Console ——————————————————————————————

// Matches the lines that progress bars are drawn on, for example "[####----] 45%"
const PROGRESS_LINE_RE = /(\d{1,3}%|\[[=#+\-.\u2500-\u259F ]{3,}\]|[\u2588\u2591\u2592\u2593]{2,})/;

/** Whether the line is a progress bar, which is redrawn repeatedly while a command runs. */
function isProgressLine(line) {
  return PROGRESS_LINE_RE.test(line);
}

/**
 * The line with all digits and progress bar characters removed, so that the consecutive
 * progress frames of the same line compare equal.
 */
function getLineShape(line) {
  return line
    .replace(/\d+/g, "#")
    .replace(/[#=+\-.\u2500-\u259F]{3,}/g, "<bar>")
    .trimEnd();
}

function setLog(text) {
  element("log").textContent = text;
  state.pendingText = "";
  state.pendingNode = null;
  state.logTail = [];
}

function appendLog(text, className) {
  const log = element("log");

  if (className) {
    const span = document.createElement("span");
    span.className = className;
    span.textContent = text;
    log.append(span);
    state.logTail = [];
    scrollLog();
    return;
  }

  const lines = (state.pendingText + text).split("\n");
  state.pendingText = lines.pop() ?? "";

  if (state.pendingNode) {
    state.pendingNode.remove();
    state.pendingNode = null;
  }

  for (const line of lines) {
    writeLine(line);
  }

  if (state.pendingText) {
    state.pendingNode = document.createTextNode(state.pendingText);
    log.append(state.pendingNode);
  }

  scrollLog();
}

function writeLine(line) {
  const log = element("log");
  const shape = getLineShape(line);
  const isProgress = isProgressLine(line);

  // Progress bars are redrawn by the command every time they change, so instead of
  // appending a new line for every frame, the previous one is updated in place. The same
  // applies to the lines around it, as long as a progress bar is nearby.
  const isProgressRegion = isProgress || state.logTail.some((entry) => entry.isProgress);

  if (isProgressRegion) {
    for (let i = state.logTail.length - 1; i >= 0; i--) {
      if (state.logTail[i].shape === shape) {
        state.logTail[i].node.nodeValue = line + "\n";
        state.logTail[i].isProgress = state.logTail[i].isProgress || isProgress;
        return;
      }
    }
  }

  const node = document.createTextNode(line + "\n");
  log.append(node);

  state.logTail.push({ node, shape, isProgress });
  if (state.logTail.length > 8) state.logTail.shift();
}

function scrollLog() {
  const log = element("log");
  if (element("autoscroll").checked) {
    log.scrollTop = log.scrollHeight;
  }
}

function clearLog() {
  setLog("Output cleared.\n");
}

function downloadLog() {
  if (!state.run) {
    showToast("There is nothing to download yet.", "warning");
    return;
  }

  window.location.href = `/api/runs/${state.run.id}/log`;
}

// —————————————————————————————— Chrome ——————————————————————————————

function setStatus(kind, text) {
  const pill = element("status");
  pill.className = `pill ${kind}`;
  pill.textContent = text;
}

function showToast(message, kind = "info") {
  const toast = document.createElement("div");
  toast.className = `toast ${kind}`;
  toast.textContent = message;
  toast.title = "Click to dismiss";
  toast.addEventListener("click", () => toast.remove());

  element("toasts").append(toast);
  setTimeout(() => toast.remove(), 8000);
}

function iconSvg(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("class", "icon");
  const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
  use.setAttribute("href", `#i-${name}`);
  svg.append(use);
  return svg;
}

function applyTheme(theme) {
  document.documentElement.dataset.theme = theme;
  const use = element("theme-toggle").querySelector("use");
  use.setAttribute("href", theme === "dark" ? "#i-sun" : "#i-moon");
}

function wireEvents() {
  element("run").addEventListener("click", () => void runSelectedCommand());
  element("cancel").addEventListener("click", () => void cancelRun());
  element("raw-run").addEventListener("click", () => void runRawCommand());
  element("raw-input").addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      void runRawCommand();
    }
  });
  element("log-clear").addEventListener("click", clearLog);
  element("log-download").addEventListener("click", downloadLog);

  element("advanced-toggle").addEventListener("change", (event) => {
    state.showAdvanced = event.target.checked;
    renderForm();
    refreshCommandLinePreview();
  });

  element("cmdline-copy").addEventListener("click", async () => {
    const text =
      element("cmdline-preview").dataset.actual ||
      element("cmdline-preview").textContent ||
      "";
    try {
      await navigator.clipboard.writeText(text);
      showToast("Command copied to the clipboard.", "success");
    } catch {
      showToast(text, "info");
    }
  });

  const tokenInput = element("token");
  tokenInput.addEventListener("input", () => {
    state.token = tokenInput.value;
    saveToken();
    refreshCommandLinePreview();
    refreshRunAvailability();
  });

  element("token-reveal").addEventListener("click", () => {
    const isPassword = tokenInput.type === "password";
    tokenInput.type = isPassword ? "text" : "password";
    element("token-reveal").title = isPassword ? "Hide token" : "Show token";
    element("token-reveal")
      .querySelector("use")
      .setAttribute("href", isPassword ? "#i-eye-off" : "#i-eye");
  });

  element("token-remember").addEventListener("change", (event) => {
    state.remember = event.target.checked;
    localStorage.setItem(REMEMBER_KEY, String(state.remember));
    saveToken();
  });

  element("token-clear").addEventListener("click", () => {
    tokenInput.value = "";
    state.token = "";
    saveToken();
    refreshCommandLinePreview();
    refreshRunAvailability();
    showToast("The saved token was removed from this browser.", "success");
  });

  element("theme-toggle").addEventListener("click", () => {
    const theme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    localStorage.setItem(THEME_KEY, theme);
    applyTheme(theme);
  });

  const log = element("log");
  log.addEventListener("scroll", () => {
    const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 40;
    element("autoscroll").checked = atBottom;
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && state.run?.state === "running") {
      event.preventDefault();
      void cancelRun();
      return;
    }

    if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
      event.preventDefault();
      void runSelectedCommand();
      return;
    }

    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "l") {
      event.preventDefault();
      clearLog();
      return;
    }

    if (event.altKey && /^[1-9]$/.test(event.key)) {
      const command = state.commands[Number(event.key) - 1];
      if (command) {
        event.preventDefault();
        selectCommand(command.name);
      }
    }
  });
}

function saveToken() {
  if (state.remember && state.token) {
    localStorage.setItem(TOKEN_KEY, state.token);
  } else {
    localStorage.removeItem(TOKEN_KEY);
  }
}
