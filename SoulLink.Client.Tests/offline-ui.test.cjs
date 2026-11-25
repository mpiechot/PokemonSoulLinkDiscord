const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

class Element {
  constructor() { this.value = ''; this.children = []; this.hidden = false; }
  set textContent(value) { this.value = String(value); this.children = []; }
  get textContent() { return this.value + this.children.map(child => child.textContent).join(''); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.value = ''; this.children = children; }
  addEventListener() {}
}

test('losing the local client removes stale demo party, run, events and online indicators', async () => {
  const elements = new Map();
  const get = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  let nextUpdate;
  let calls = 0;
  const fixture = {
    state: 'Ready', bridge: 'Online', registered: true, player: 'Marco (demo)', bridgePort: 5091,
    server: 'http://127.0.0.1:5080', log: [],
    snapshot: { areaName: 'Route 101', gameCode: 'AXVD', layoutId: 'synthetic-fixture-v1', pokemon: [
      { container: 'Party', slot: 0, displayName: 'bulbasaur', nickname: 'A', level: 5, hp: 20, maxHp: 20 },
    ] },
    run: { name: 'Three-player Ruby demo', phase: 'Active', players: [], lines: ['demo run'],
      history: [{ at: '2026-01-01', message: 'demo catch' }] },
  };
  const context = {
    document: { getElementById: get, createElement: () => new Element() },
    fetch: async () => { if (++calls > 1) throw new Error('Disconnected'); return { ok: true, json: async () => fixture }; },
    setTimeout: callback => { nextUpdate = callback; }, console,
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../SoulLink.Client/wwwroot/app.js'), 'utf8'), context);
  await new Promise(resolve => setImmediate(resolve));
  assert.match(get('party').textContent, /bulbasaur/);
  await nextUpdate();
  assert.doesNotMatch(get('party').textContent, /bulbasaur/);
  assert.doesNotMatch(get('player').textContent, /demo/);
  assert.doesNotMatch(get('run-name').textContent, /demo/);
  assert.equal(get('run').textContent, '');
  assert.equal(get('events').textContent, '');
  assert.notEqual(get('bridge').textContent, 'Online');
});
