const fs = require('fs');
const html = fs.readFileSync('arrow-mystic.html', 'utf8');
const js = html.match(/<script>([\s\S]*?)<\/script>/)[1];
let fail = 0;
const ok = (label, pass, detail) => {
  console.log((pass ? '  ok   ' : 'FAIL   ') + label + (detail ? '  ' + detail : ''));
  if (!pass) fail++;
};

try { new Function(js); ok('JS parses', true); }
catch (e) { ok('JS parses', false, e.message); }

const f = js.match(/const FS=`([\s\S]*?)`;/)[1];
const cnt = (s, re) => (s.match(re) || []).length;
ok('GLSL braces balanced', cnt(f, /\{/g) === cnt(f, /\}/g), cnt(f, /\{/g) + '/' + cnt(f, /\}/g));
ok('GLSL parens balanced', cnt(f, /\(/g) === cnt(f, /\)/g), cnt(f, /\(/g) + '/' + cnt(f, /\)/g));
ok('no stray backticks in shader', cnt(f, /`/g) === 0);

// Branch-chain integrity. Balanced braces do NOT imply a valid else-chain: closing the
// group one branch early orphans every later `else if` while the totals still match.
// This broke the published page TWICE by appending branches past the group closer, and
// the first version of this check only printed the distinct depths — which I then
// dismissed as a known false positive. So it now asserts a hard rule instead of
// reporting a number: at most ONE depth change across the whole sorted chain.
const lines = f.split('\n');
let depth = 0;
const br = [];
for (const line of lines) {
  const code = line.replace(/\/\/.*$/, '');
  const m = code.match(/\b(?:else\s+)?if\s*\(\s*St\s*==\s*(\d+)\s*\)/);
  if (m) br.push({ id: +m[1], depth });
  for (const ch of code) { if (ch === '{') depth++; else if (ch === '}') depth--; }
}
br.sort((a, b) => a.id - b.id);
const breaks = [];
for (let i = 1; i < br.length; i++) {
  if (br[i].depth !== br[i - 1].depth) {
    breaks.push('St' + br[i - 1].id + '(d' + br[i - 1].depth + ') -> St' +
                br[i].id + '(d' + br[i].depth + ')');
  }
}
ok('branch chain contiguous', breaks.length <= 1,
   breaks.length ? breaks.join(' | ') : 'one expected nesting step');

const ids = br.map(b => b.id);
const N = (js.match(/^ \[/gm) || []).length;
const missing = [];
for (let i = 0; i < N; i++) if (!ids.includes(i)) missing.push(i);
ok('branch coverage 0..' + (N - 1), missing.length === 0,
   missing.length ? 'MISSING ' + missing.join(',') : N + ' branches');

ok('JS PROGD matches style count',
   js.match(/const PROGD=\[([^\]]*)\]/)[1].split(',').length === N);
const gm = js.match(/for\(let i=0;i<(\d+);i\+\+\)/);
ok('grid cell count matches', !!gm && +gm[1] === N, gm ? gm[1] : 'not found');

const reserved = ['flat','smooth','sample','filter','input','output','buffer','shared',
  'image','active','packed','common','noperspective','centroid','patch','invariant',
  'layout','precision','resource','partition','namespace','using','cast','sizeof'];
const bad = reserved.filter(r =>
  new RegExp('\\b(float|int|vec[234]|bool|mat[234]|uint)[ \\t]+' + r + '\\b').test(f));
ok('no reserved-word identifiers', bad.length === 0, bad.join(', '));

// Undeclared identifiers. An earlier edit deleted the line declaring `dirH` and a later
// edit referenced it again — the shader failed to compile and every structural check
// above still passed, because none of them resolve names.
{
  const src = f.replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
  const TYPE = '(?:float|int|uint|bool|void|vec[234]|ivec[234]|uvec[234]|bvec[234]|mat[234]|sampler2D)';
  const declared = new Set();
  // `type a, b, c;` including initialisers that contain calls:
  //   float f1=aa(d), f2=aa(d+w), f3=...;
  // Capture through to the semicolon, then split on TOP-LEVEL commas only — splitting
  // naively, or stopping at the first '(', silently loses every declarator after the
  // first and reports them as undeclared.
  const splitTop = s => {
    const out = []; let depth = 0, cur = '';
    for (const ch of s) {
      if (ch === '(' || ch === '[') depth++;
      else if (ch === ')' || ch === ']') depth--;
      if (ch === ',' && depth === 0) { out.push(cur); cur = ''; } else cur += ch;
    }
    out.push(cur); return out;
  };
  for (const m of src.matchAll(new RegExp(
      '\\b(?:uniform\\s+|const\\s+|in\\s+|out\\s+)*' + TYPE + '\\s+([^;{}]+);', 'g'))) {
    for (const part of splitTop(m[1])) {
      const id = part.trim().match(/^([A-Za-z_]\w*)/);
      if (id) declared.add(id[1]);
    }
  }
  // function names and their parameters
  for (const m of src.matchAll(new RegExp(TYPE + '\\s+([A-Za-z_]\\w*)\\s*\\(([^)]*)\\)', 'g'))) {
    declared.add(m[1]);
    for (const par of m[2].split(',')) {
      const id = par.trim().match(/([A-Za-z_]\w*)\s*$/);
      if (id) declared.add(id[1]);
    }
  }
  // loop counters
  for (const m of src.matchAll(/for\s*\(\s*\w+\s+([A-Za-z_]\w*)/g)) declared.add(m[1]);

  const BUILTIN = new Set(('sin cos tan asin acos atan sinh cosh tanh pow exp log exp2 log2 ' +
    'sqrt inversesqrt abs sign floor trunc round ceil fract mod modf min max clamp mix step ' +
    'smoothstep length distance dot cross normalize faceforward reflect refract matrixCompMult ' +
    'lessThan greaterThan equal notEqual any all not texture texture2D textureLod textureProj ' +
    'dFdx dFdy fwidth radians degrees isnan isinf discard return if else for while do break ' +
    'continue struct true false const uniform in out inout attribute varying precision highp ' +
    'mediump lowp invariant layout main gl_Position gl_FragCoord gl_FragColor gl_PointSize ' +
    'gl_FrontFacing float int uint bool void vec2 vec3 vec4 ivec2 ivec3 ivec4 uvec2 uvec3 ' +
    'uvec4 bvec2 bvec3 bvec4 mat2 mat3 mat4 sampler2D version es x y z w r g b a s t p q xy ' +
    'xyz xyzw rgb rgba').split(/\s+/));

  const unknown = new Set();
  for (const m of src.matchAll(/\b([A-Za-z_]\w*)\b/g)) {
    const id = m[1];
    if (declared.has(id) || BUILTIN.has(id)) continue;
    if (/^\d/.test(id)) continue;
    unknown.add(id);
  }
  ok('no undeclared identifiers', unknown.size === 0, [...unknown].join(', '));
}

// Call arity. GLSL has no overloading, so changing a function's signature and missing a
// call site is a hard compile error. Adding an `out` parameter to headD left one caller
// on the old one-arg form and every check above still passed.
{
  const src = f.replace(/\/\/.*$/gm, '').replace(/\/\*[\s\S]*?\*\//g, '');
  const TYPE = '(?:float|int|uint|bool|void|vec[234]|ivec[234]|bvec[234]|mat[234])';
  const argCount = s => {
    const t = s.trim();
    if (!t) return 0;
    let depth = 0, n = 1;
    for (const ch of t) {
      if (ch === '(' || ch === '[') depth++;
      else if (ch === ')' || ch === ']') depth--;
      else if (ch === ',' && depth === 0) n++;
    }
    return n;
  };
  const sig = new Map();
  for (const m of src.matchAll(new RegExp(TYPE + '\\s+([A-Za-z_]\\w*)\\s*\\(([^)]*)\\)\\s*\\{', 'g'))) {
    sig.set(m[1], argCount(m[2]));
  }
  const bad = [];
  for (const name of sig.keys()) {
    const re = new RegExp('\\b' + name + '\\s*\\(', 'g');
    let m;
    while ((m = re.exec(src))) {
      // skip the definition itself
      const before = src.slice(Math.max(0, m.index - 24), m.index);
      if (new RegExp(TYPE + '\\s+$').test(before)) continue;
      let i = m.index + m[0].length, depth = 1, args = '';
      while (i < src.length && depth > 0) {
        const ch = src[i];
        if (ch === '(') depth++;
        else if (ch === ')') { depth--; if (!depth) break; }
        args += ch; i++;
      }
      const got = argCount(args), want = sig.get(name);
      if (got !== want) bad.push(name + '(' + got + ') expected ' + want);
    }
  }
  ok('call arity matches definitions', bad.length === 0, [...new Set(bad)].join(', '));
}

console.log(fail ? '\n' + fail + ' CHECK(S) FAILED' : '\nall checks passed (' + N + ' styles)');
process.exit(fail ? 1 : 0);
