
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { compile } from "strucpp";
import { discoverStlibs } from "strucpp/node";

const args = process.argv.slice(2);
const flag = (name, def) => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : def;
};
const [stDir, outDir] = args.filter((a, i) => !a.startsWith("--") && !(args[i - 1] || "").startsWith("--"));
if (!stDir || !outDir) {
  console.error(
    "usage: node build-bundle.mjs <st-dir> <out-dir> [--interval T#1ms] [--modbus-port 502]" +
      " [--opcua-port 4840] [--opcua-cycle-ms 100]",
  );
  process.exit(2);
}
const interval = flag("--interval", "T#1ms");
const modbusPort = Number(flag("--modbus-port", "502"));
const opcuaPort = flag("--opcua-port", "") ? Number(flag("--opcua-port", "")) : null;
const opcuaCycleMs = Number(flag("--opcua-cycle-ms", "100"));

const require = createRequire(import.meta.url);
const pkgDir = path.dirname(require.resolve("strucpp/package.json"));
const pkgVersion = JSON.parse(fs.readFileSync(path.join(pkgDir, "package.json"), "utf8")).version;

const pouFiles = fs.readdirSync(stDir).filter((f) => f.toLowerCase().endsWith(".st")).sort();
const pous = pouFiles.map((f) => fs.readFileSync(path.join(stDir, f), "utf8"));
const configuration = `
CONFIGURATION Config0

  RESOURCE Res0 ON PLC
    TASK task0(INTERVAL := ${interval},PRIORITY := 1);
    PROGRAM instance0 WITH task0 : main;
  END_RESOURCE
END_CONFIGURATION
`;
const programSt = pous.join("\n\n") + "\n" + configuration;
const md5 = createHash("md5").update(programSt).digest("hex");

const libraries = discoverStlibs(path.join(pkgDir, "libs"));
const result = compile(programSt, {
  headerFileName: "generated.hpp",
  fileName: "program.st",
  debug: true,
  lineMapping: true,
  libraries,
  md5,
  pouIncludes: [],
});
for (const w of result.warnings) console.error(`warning ${w.line}:${w.column} ${w.message}`);
if (!result.success) {
  for (const e of result.errors) console.error(`error ${e.file ?? "program.st"}:${e.line}:${e.column} ${e.message}`);
  process.exit(4);
}

const files = {};
files["program.st"] = programSt;
if (result.cppFiles && result.cppFiles.length > 0) {
  for (const f of result.cppFiles) files[f.name] = f.content;
} else {
  files["generated.cpp"] = result.cppCode;
}
files["generated.hpp"] = result.headerCode;
if (result.debugTableCpp !== undefined) files["generated_debug.cpp"] = result.debugTableCpp;
if (result.debugMap !== undefined) files["debug-map.json"] = JSON.stringify(result.debugMap, null, 2);
const incDir = path.join(pkgDir, "src", "runtime", "include");
for (const h of fs.readdirSync(incDir)) {
  files[`strucpp_runtime/include/${h}`] = fs.readFileSync(path.join(incDir, h), "utf8");
}
files["defines.h"] = `#pragma once\n// Program MD5\n#define PROGRAM_MD5 "${md5}"\n`;
files["c_blocks.h"] = "// Empty file\n";
files["conf/modbus_slave.json"] = JSON.stringify(
  {
    network_configuration: { host: "0.0.0.0", port: modbusPort },
    buffer_mapping: {
      holding_registers: { qw_count: 1024, mw_count: 1024, md_count: 1024, ml_count: 1024 },
      coils: { qx_bits: 8192, mx_bits: 0 },
      discrete_inputs: { ix_bits: 8192 },
      input_registers: { iw_count: 1024 },
    },
  },
  null,
  2,
);
let opcuaNodes = [];
if (opcuaPort !== null) {
  const opcua = opcuaConfig(result, programSt, { port: opcuaPort, cycleTimeMs: opcuaCycleMs });
  files["conf/opcua.json"] = JSON.stringify(opcua, null, 2);
  opcuaNodes = opcua[0].config.address_space.variables.map((v) => v.node_id);
}

fs.rmSync(outDir, { recursive: true, force: true });
for (const [rel, content] of Object.entries(files)) {
  const p = path.join(outDir, rel);
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, content);
}
console.log(
  JSON.stringify({
    strucpp: pkgVersion,
    interval,
    md5,
    files: Object.keys(files).filter((k) => !k.startsWith("strucpp_runtime/")),
    ...(opcuaPort !== null ? { opcua: { port: opcuaPort, cycleTimeMs: opcuaCycleMs, nodes: opcuaNodes } } : {}),
  }),
);

function opcuaConfig(result, source, options = {}) {
  const o = {
    port: 4840,
    bindAddress: "0.0.0.0",
    endpointPath: "/openplc/opcua",
    serverName: "OpenPLC OPC UA Server",
    applicationUri: "urn:freeopcua:python:server",
    productUri: "urn:openplc:runtime",
    namespaceUri: "urn:stormbench:plc",
    cycleTimeMs: 100,
    anonymousRole: "viewer",
    permissions: { viewer: "r", operator: "r", engineer: "r" },
    ...options,
  };
  if (!Number.isInteger(o.port) || o.port < 1 || o.port > 65535) throw new Error(`opcua: bad port ${o.port}`);
  if (!Number.isInteger(o.cycleTimeMs) || o.cycleTimeMs < 1) throw new Error(`opcua: bad cycle time ${o.cycleTimeMs}`);
  if (o.cycleTimeMs < 10 || o.cycleTimeMs > 10000) {
    console.error(`warning opcua: cycle_time_ms ${o.cycleTimeMs} is outside the editor's range 10..10000`);
  }

  const validTypes = new Set(["BOOL", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT", "REAL",
    "LREAL", "BYTE", "WORD", "DWORD", "LWORD", "STRING", "WSTRING", "TIME", "DATE", "TOD", "DT"]);
  const roles = new Set(["viewer", "operator", "engineer"]);
  if (!roles.has(o.anonymousRole)) throw new Error(`opcua: bad anonymous role ${o.anonymousRole}`);

  const map = result.debugMap;
  if (!map || map.version !== 2 || !Array.isArray(map.leaves)) throw new Error("opcua: no debug-map.json v2");
  const leaves = new Map(map.leaves.map((leaf) => [leaf.path.toUpperCase(), leaf]));

  const lines = source.split(/\r?\n/);
  const slice = (span) => lines[span.startLine - 1].slice(span.startCol - 1, span.endCol);
  const spelling = (upper, span) => {
    const text = span ? slice(span) : "";
    return text.toUpperCase() === upper ? text : upper;
  };
  const programSpelling = (prog) => {
    const m = lines[prog.sourceSpan.startLine - 1].match(/\bPROGRAM\s+([A-Za-z_]\w*)/i);
    return m && m[1].toUpperCase() === prog.name.toUpperCase() ? m[1] : prog.name;
  };
  const comment = (decl) => {
    const span = decl.sourceSpan;
    let text = lines[span.endLine - 1].slice(span.endCol);
    const start = text.indexOf("(*");
    if (start < 0 || text.slice(0, start).trim() !== "") return "";
    text = text.slice(start + 2);
    for (let n = span.endLine; !text.includes("*)") && n < lines.length && n < span.endLine + 10; n++) {
      text += " " + lines[n];
    }
    const end = text.indexOf("*)");
    return (end >= 0 ? text.slice(0, end) : "").replace(/\s+/g, " ").trim();
  };

  const instances = [];
  for (const cfg of result.ast.configurations ?? []) {
    for (const res of cfg.resources ?? []) {
      for (const pi of res.programInstances ?? []) instances.push({ name: pi.instanceName, program: pi.programType });
    }
  }

  const variables = [];
  const nodeIds = new Set();
  for (const prog of result.ast.programs ?? []) {
    const instance = instances.find((i) => i.program.toUpperCase() === prog.name.toUpperCase());
    if (!instance) continue;
    const pouName = programSpelling(prog);
    for (const block of prog.varBlocks) {
      if (block.blockType === "VAR_EXTERNAL") continue;
      for (const decl of block.declarations) {
        if (decl.addressKind !== "direct" || !String(decl.address ?? "").startsWith("%")) continue;
        decl.names.forEach((upper, k) => {
          const name = spelling(upper, decl.nameSpans?.[k]);
          const debugPath = `${instance.name}.${upper}`.toUpperCase();
          const leaf = leaves.get(debugPath);
          if (!leaf) throw new Error(`opcua: ${pouName}:${name} has no leaf ${debugPath} in debug-map.json`);
          if (!validTypes.has(String(leaf.type).toUpperCase())) {
            throw new Error(`opcua: ${pouName}:${name} has type ${leaf.type}, not supported by the plugin`);
          }
          const nodeId = `PLC.${pouName}.${name}`;
          if (nodeId.length > 128 || name.length > 64) throw new Error(`opcua: node id or browse name too long: ${nodeId}`);
          if (nodeIds.has(nodeId.toLowerCase())) throw new Error(`opcua: duplicate node id ${nodeId}`);
          nodeIds.add(nodeId.toLowerCase());
          const note = comment(decl);
          variables.push({
            node_id: nodeId,
            browse_name: name,
            display_name: name,
            datatype: leaf.type,
            size: leaf.size,
            description: note ? `${decl.address}: ${note}` : decl.address,
            arr: leaf.arrayIdx,
            elem: leaf.elemIdx,
            permissions: { ...o.permissions },
          });
        });
      }
    }
  }
  if (variables.length === 0) throw new Error("opcua: no located variables found");

  return [
    {
      name: "opcua_server",
      protocol: "OPC-UA",
      config: {
        format_version: 2,
        server: {
          name: o.serverName,
          application_uri: o.applicationUri,
          product_uri: o.productUri,
          endpoint_url: `opc.tcp://${o.bindAddress}:${o.port}${o.endpointPath}`,
          security_profiles: [
            {
              name: "insecure",
              enabled: true,
              security_policy: "None",
              security_mode: "None",
              auth_methods: ["Anonymous"],
              anonymous_role: o.anonymousRole,
            },
          ],
        },
        security: {
          server_certificate_strategy: "auto_self_signed",
          server_certificate_custom: null,
          server_private_key_custom: null,
          trusted_client_certificates: [],
        },
        users: [],
        cycle_time_ms: o.cycleTimeMs,
        address_space: {
          namespace_uri: o.namespaceUri,
          variables,
          structures: [],
          arrays: [],
        },
      },
    },
  ];
}
