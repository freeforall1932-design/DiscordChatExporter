// End-to-end test for the web interface served by "DiscordChatExporter.Cli gui".
//
// The test loads the real page into a DOM, drives it like a user would (clicking buttons,
// filling in options, running commands) and checks both what the page shows and what the
// server reports through its API.
//
// Usage:
//   node test.mjs                        # against http://127.0.0.1:5000
//   DCE_GUI_URL=http://localhost:5155 node test.mjs
//   DCE_GUI_USE_LOCAL_ASSETS=1 node test.mjs   # use the assets from the working copy
import { JSDOM, VirtualConsole } from "jsdom";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const BASE = process.env.DCE_GUI_URL || "http://127.0.0.1:5000";
const USE_LOCAL = process.env.DCE_GUI_USE_LOCAL_ASSETS === "1";
const LOCAL_DIR =
  process.env.DCE_GUI_ASSETS_DIR ||
  join(dirname(fileURLToPath(import.meta.url)), "..", "DiscordChatExporter.Cli", "Gui", "Assets");

const failures = [];
let checks = 0;

function check(name, condition, extra = "") {
  checks++;
  if (condition) {
    console.log(`  ok   ${name}`);
  } else {
    console.log(`  FAIL ${name}${extra ? " -> " + extra : ""}`);
    failures.push(name + (extra ? " -> " + extra : ""));
  }
}

const section = (name) => console.log(`\n${name}`);

const readLocal = async (name) => readFile(`${LOCAL_DIR}/${name}`, "utf8");

const html = USE_LOCAL ? await readLocal("index.html") : await (await fetch(`${BASE}/`)).text();
const script = USE_LOCAL
  ? await readLocal("app.js")
  : await (await fetch(`${BASE}/app.js`)).text();

const virtualConsole = new VirtualConsole();
const pageErrors = [];
virtualConsole.on("jsdomError", (e) => pageErrors.push(String(e)));
virtualConsole.on("error", (...args) => pageErrors.push(args.join(" ")));

const dom = new JSDOM(html, {
  url: `${BASE}/`,
  runScripts: "outside-only",
  pretendToBeVisual: true,
  virtualConsole,
});

const { window } = dom;
window.fetch = (input, init) => fetch(new URL(input, BASE).toString(), init);
window.localStorage.clear();

window.eval(script + "\nwindow.__testLog = { append: appendLog, set: setLog }; window.__state = state;");

const $ = (id) => window.document.getElementById(id);
const $$ = (selector, root = window.document) => [...root.querySelectorAll(selector)];
const click = (element) =>
  element.dispatchEvent(new window.MouseEvent("click", { bubbles: true }));
const type = (element, value) => {
  element.value = value;
  element.dispatchEvent(new window.Event("input", { bubbles: true }));
};
const commandButton = (name) => $(`command-list`).querySelector(`[data-command="${name}"]`);

const waitFor = async (predicate, description, timeout = 25000) => {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (predicate()) return true;
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  console.log(`  FAIL timed out: ${description}`);
  failures.push(`timeout: ${description}`);
  return false;
};

const waitForAsync = async (predicate, description, timeout = 30000) => {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (await predicate()) return true;
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  console.log(`  FAIL timed out: ${description}`);
  failures.push(`timeout: ${description}`);
  return false;
};

const api = async (path, options) => {
  const response = await fetch(new URL(path, BASE).toString(), options);
  const text = await response.text();
  let json = null;
  try {
    json = JSON.parse(text);
  } catch {}
  return { status: response.status, json, text };
};

// —————————————————————————————————— UI ——————————————————————————————————

section("1. startup");
await waitFor(() => $$(".command").length > 0, "the command list to load");

const commands = $$(".command").map((b) => b.dataset.command);
console.log("  commands:", commands.join(", "));
check("all 8 CLI commands are exposed as buttons", commands.length === 8, commands.join(","));
check("the gui command is not listed", !commands.includes("gui"));
check(
  "commands are grouped",
  $$(".command-group, .group-title, .group").length > 0,
  "no group headings found"
);
check("a command is selected on load", $("command-name").textContent.length > 0);
check("the version is displayed", $("brand-meta").textContent.includes("999.9.9"));
check("the working directory is displayed", $("working-dir").textContent.length > 1);
check("the command line preview is rendered", $("cmdline-preview").textContent.length > 10);

section("2. token field");
type($("token"), "test-token-value");
check("the token is stored locally", window.localStorage.getItem("discordchatexporter.token") === "test-token-value");
click($("token-reveal"));
check("the reveal button shows the token", $("token").type === "text");
click($("token-reveal"));
check("the reveal button hides the token", $("token").type === "password");
click($("token-clear"));
check("the clear button forgets the token", $("token").value === "");
type($("token"), "test-token-value");

section("3. switching commands");
click(commandButton("channels"));
await waitFor(() => $("command-name").textContent === "channels", "channels to be selected");
check("the title is shown", $("command-title").textContent.length > 0);
check("a token badge is shown for commands that need one", !$("command-token-badge").classList.contains("hidden"));
check("the form is generated", $$(".field").length >= 3, `${$$(".field").length} fields`);
check(
  "the enum option renders as a select",
  [...$$("select")].some((s) => s.name === "include-threads" || s.id.includes("include-threads"))
);
check("validation is reported", $("validation").textContent.length > 0);
check("run is disabled while required options are missing", $("run").disabled === true);
check(
  "the command line preview does not leak the token",
  !$("cmdline-preview").textContent.includes("test-token-value")
);

section("4. filling in options");
type($("option-guild"), "123456789012345678");
check(
  "the preview updates while typing",
  $("cmdline-preview").textContent.includes("--guild 123456789012345678")
);
check("run is enabled once the form is valid", $("run").disabled === false);

const includeVc = $("option-include-vc");
if (includeVc) {
  const before = includeVc.checked;
  includeVc.checked = !before;
  includeVc.dispatchEvent(new window.Event("change", { bubbles: true }));
  check(
    "boolean options are reflected in the preview",
    $("cmdline-preview").textContent.includes(`--include-vc ${!before}`)
  );
}

section("5. sequence options (chips)");
click(commandButton("export"));
await waitFor(() => $("command-name").textContent === "export", "export to be selected");

const channelInput = $("option-channel");
check("the channel option accepts multiple values", channelInput !== null);
type(channelInput, "111111111111111111");
channelInput.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
type(channelInput, "222222222222222222,333333333333333333");
channelInput.dispatchEvent(new window.Event("blur", { bubbles: true }));

const chips = $$(".chip").map((c) => c.textContent.replace(/[×✕x]/g, "").trim());
console.log("  chips:", JSON.stringify(chips));
check("typed and pasted values become chips", chips.length === 3, chips.join(","));
check(
  "sequence options are repeated on the command line",
  $("cmdline-preview").textContent.includes("--channel 111111111111111111 --channel 222222222222222222")
);

section("6. running a command");
click(commandButton("guide"));
await waitFor(() => $("command-name").textContent === "guide", "guide to be selected");
check("guide does not ask for a token", $("command-token-badge").classList.contains("hidden"));
check("guide can be run", $("run").disabled === false);

// The token is filled in, so that commands that don't accept --token must still work
// (regression test: "--token" used to be passed to every command, including "guide")
type($("token"), "test-token-value");

click($("run"));
await waitFor(() => /^Finished/.test($("status").textContent), "the run to finish", 30000);

const log = $("log").textContent;
console.log("  log:", JSON.stringify(log.slice(0, 100)));
check("the output is streamed into the console", log.includes("developer tools"));
check("the executed command line is shown", log.includes("DiscordChatExporter.Cli guide"));
check("the status is updated", /^Finished/.test($("status").textContent), $("status").textContent);
check("the exit code is shown", $("run-badge").textContent.includes("0"), $("run-badge").textContent);
check("run is available again", $("run").disabled === false);
check("the log can be downloaded", $("log-download").disabled === false);

section("7. raw command line");
// Waits until both the server and the page agree that nothing is running, so that the
// next command isn't refused as "still running"
const idle = () =>
  waitForAsync(
    async () =>
      (await api("/api/runs")).json.every((r) => r.state !== "running") &&
      window.__state?.run?.state !== "running",
    "the server and the page to be idle"
  );

const rawInput = $("raw-input");
if (rawInput) {
  await idle();
  type(rawInput, "guide");
  click($("raw-run"));
  await waitFor(() => $("log").textContent.startsWith("$ DiscordChatExporter.Cli guide"), "the raw run to start", 15000);
  await waitFor(() => /^Finished/.test($("status").textContent), "the raw run to finish", 30000);
  check("raw commands are streamed too", $("log").textContent.includes("developer tools"));

  await idle();
  type(rawInput, "nonsense-command");
  click($("raw-run"));
  await waitFor(() => $("log").textContent.includes("Unrecognized"), "the error to be reported", 30000);
  check("unknown commands are reported in the console", $("log").textContent.includes("Unrecognized"));
  await waitFor(() => $("status").textContent.startsWith("Failed"), "the run to fail", 15000);
} else {
  check("the raw command input exists", false);
}

section("8. validation errors");
await idle();
click(commandButton("export"));
await waitFor(() => $("command-name").textContent === "export", "export to be selected");
type($("option-channel"), "");
for (const chip of $$(".chip-remove")) click(chip);
type($("token"), "");
check("export is disabled without a token", $("run").disabled === true, $("validation").textContent);
type($("token"), "test-token-value");

section("8b. progress frames");
if (window.__testLog) {
  window.__testLog.set("");
  for (let i = 10; i <= 90; i += 10) {
    window.__testLog.append(`Exporting #general (1/1)\n[${"#".repeat(i / 10)}${"-".repeat(9 - i / 10)}] ${i}%\n`);
  }
  const rendered = $("log").textContent;
  const percentMatches = [...rendered.matchAll(/(\d+)%/g)].map((m) => m[1]);
  check(
    "progress frames are redrawn in place",
    percentMatches.length <= 3 && percentMatches.includes("90"),
    `rendered ${percentMatches.length} frames: ${JSON.stringify(rendered)}`
  );
  check("the completed frame is kept", rendered.includes("90%"), JSON.stringify(rendered));
} else {
  check("the log renderer is reachable from the outside", false);
}

section("9. theme");
const initialTheme = window.document.documentElement.dataset.theme;
click($("theme-toggle"));
check(
  "the theme toggles",
  window.document.documentElement.dataset.theme !== initialTheme,
  window.document.documentElement.dataset.theme
);
check("the theme is persisted", window.localStorage.getItem("discordchatexporter.theme") !== null);

section("10. advanced options");
const basicCount = $$(".field").length;
if ($("advanced-toggle")) {
  $("advanced-toggle").checked = true;
  $("advanced-toggle").dispatchEvent(new window.Event("change", { bubbles: true }));
  check("advanced options are revealed", $$(".field").length > basicCount, `${basicCount} -> ${$$(".field").length}`);
}

section("11. keyboard shortcuts");
click(commandButton("guilds"));
window.document.dispatchEvent(
  new window.KeyboardEvent("keydown", { key: "2", altKey: true, bubbles: true })
);
await waitFor(() => $("command-name").textContent === "channels", "alt+2 to select the second command");
check("alt+number switches commands", $("command-name").textContent === "channels");

check("no uncaught page errors", pageErrors.length === 0, pageErrors.join(" | "));

// ————————————————————————————————— API —————————————————————————————————

section("12. server API");
const info = await api("/api/info");
check("GET /api/info returns the command catalog", info.status === 200 && info.json?.commands?.length === 8);
check(
  "the catalog carries no token option",
  !JSON.stringify(info.json).includes('"name":"token"'),
  "token option leaked into the catalog"
);
check(
  "options carry a kind and a label",
  info.json.commands.every((c) => c.options.every((o) => o.kind && o.label))
);
const exportCommand = info.json.commands.find((c) => c.name === "export");
const kinds = Object.fromEntries(exportCommand.options.map((o) => [o.name, o.kind]));
console.log("  export option kinds:", JSON.stringify(kinds));
check("channels are a list option", kinds.channel === "list", kinds.channel);
check("format is a select", kinds.format === "select", kinds.format);
check("output is a path", kinds.output === "path", kinds.output);
check("media is a boolean", kinds.media === "bool", kinds.media);
check("after is a date", kinds.after === "date", kinds.after);
check("export claims to need a token", exportCommand.requiresToken === true);

const runsList = await api("/api/runs");
check("GET /api/runs returns the run history", runsList.status === 200 && Array.isArray(runsList.json));

const invalid = await api("/api/runs", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({ command: "channels", options: {} }),
});
check("invalid requests are rejected", invalid.status === 400 && invalid.json?.details?.length > 0, JSON.stringify(invalid.json));

const unknown = await api("/api/runs", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({ command: "nope" }),
});
check("unknown commands are rejected", unknown.status === 400, String(unknown.status));

const crossOrigin = await api("/api/info", { headers: { origin: "https://evil.example.com" } });
check("cross-origin requests are rejected", crossOrigin.status === 403, String(crossOrigin.status));

const guideRun = await api("/api/runs", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({ command: "guide", options: {}, token: "test-token-value" }),
});
check("guide runs with an unrelated token present", guideRun.status === 201, JSON.stringify(guideRun.json));

let finalRun = null;
if (guideRun.status === 201) {
  const runId = guideRun.json.id;
  for (let i = 0; i < 60; i++) {
    finalRun = await api(`/api/runs/${runId}?cursor=0&wait=1000`);
    if (finalRun.json.state !== "running") break;
  }
  check("the run finishes successfully", finalRun.json.state === "succeeded", JSON.stringify(finalRun.json).slice(0, 300));
  check("the output is captured", /developer tools/i.test(finalRun.json.output), finalRun.json.output.slice(0, 200));
  check("the command line is reported", finalRun.json.commandLine.includes("DiscordChatExporter.Cli guide"));
  check("the token is not present in the command line", !finalRun.json.commandLine.includes("test-token-value"));

  const logFile = await api(`/api/runs/${runId}/log`);
  check("the log can be downloaded from the server", logFile.status === 200 && /developer tools/i.test(logFile.text));
}

console.log(`\n${checks - failures.length}/${checks} checks passed`);
if (failures.length > 0) {
  console.log("\nFailures:");
  for (const failure of failures) console.log(`  - ${failure}`);
  process.exit(1);
}
console.log("UI TEST PASSED");
