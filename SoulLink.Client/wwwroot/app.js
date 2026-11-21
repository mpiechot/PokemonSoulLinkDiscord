const byId = id => document.getElementById(id);
function node(tag, value, className) { const element = document.createElement(tag); element.textContent = value; if(className) element.className = className; return element; }
async function post(path, payload) {
  const response = await fetch(path, {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(payload || {})});
  if(!response.ok) { const result = await response.json(); throw new Error(result.error || 'Anfrage fehlgeschlagen.'); }
}
byId('join').addEventListener('submit', async event => {
  event.preventDefault(); byId('join-error').textContent='';
  const button = event.target.querySelector('button'); button.disabled=true;
  try { await post('/api/join', Object.fromEntries(new FormData(event.target))); }
  catch(error) { byId('join-error').textContent=error.message; }
  finally { button.disabled=false; }
});
byId('reconnect').addEventListener('click', async () => { try { await post('/api/reconnect'); } catch(error) { byId('state').textContent=error.message; } });
async function update() {
  try {
    const response = await fetch('/api/status'); if(!response.ok) throw new Error(); const status = await response.json();
    byId('state').textContent=status.state; byId('bridge').textContent=status.bridge;
    byId('reconnect').disabled=false;
    document.title=status.isDemo ? 'SoulLink Entwicklungsdemo' : 'SoulLink Diagnose';
    byId('mode').textContent=status.isDemo ? 'ENTWICKLUNGSDEMO · TESTDATEN' : 'DEIN SPIEL · NUR LESEN';
    byId('registration').hidden=status.registered; byId('player').textContent=status.player ? status.player+' · Team' : 'Dein Team';
    const snapshot = status.snapshot;
    byId('map').textContent=snapshot ? `${snapshot.areaName} · Map ${snapshot.mapGroup}:${snapshot.mapNumber} · ${snapshot.gameCode}` : 'Noch kein Spielzustand';
    const party=byId('party'); party.replaceChildren();
    for(let slot=0;slot<6;slot++) {
      const pokemon=snapshot?.pokemon.find(p=>p.container==='Party'&&p.slot===slot);
      const card=node('div','', 'pokemon');
      if(pokemon) { card.append(node('b',`${slot+1}. ${pokemon.displayName}${pokemon.isShiny?' ✦':''}`)); card.append(node('small',`${pokemon.nickname} · Lv ${pokemon.level} · HP ${pokemon.hp}/${pokemon.maxHp}`)); }
      else card.append(node('span',`${slot+1}. Freier Platz`,'empty'));
      party.append(card);
    }
    byId('inventory').textContent=snapshot ? `${snapshot.pokemon.filter(p=>p.container==='Box').length} Pokémon in der Box · Profil: ${snapshot.layoutId}` : 'Lade das SoulLink-Lua-Skript in mGBA.';
    byId('run-name').textContent=status.run?.name || 'Gemeinsamer Run'; byId('run-state').textContent=status.run?.phase || 'Noch nicht verbunden.';
    byId('run').textContent=status.run?.lines.join('\n') || '';
    const events=byId('events'); events.replaceChildren();
    for(const event of (status.run?.history || []).slice().reverse()) { const row=node('div','', 'event'); row.append(node('time',new Date(event.at).toLocaleTimeString())); row.append(node('span',event.message)); events.append(row); }
    byId('diagnostics').textContent=`Lua: 127.0.0.1:${status.bridgePort}\nServer: ${status.server}\nLetztes Paket: ${status.lastBridge || '—'}\n${(status.run?.players || []).map(p=>`${p.name}: ${p.id}`).join('\n')}\n\n${status.log.join('\n')}`;
  } catch {
    byId('state').textContent='Lokaler Client nicht erreichbar.';
    byId('bridge').textContent='Nicht verbunden';
    byId('player').textContent='Dein Team';
    byId('map').textContent='Kein aktueller Spielzustand';
    byId('party').replaceChildren();
    byId('inventory').textContent='Starte den Spieler-Client, um dein Spiel zu verbinden.';
    byId('run-name').textContent='Gemeinsamer Run';
    byId('run-state').textContent='Nicht verbunden';
    byId('run').textContent='';
    byId('events').replaceChildren();
    byId('diagnostics').textContent='Keine Verbindung zum lokalen Client.';
    byId('reconnect').disabled=true;
  }
  setTimeout(update, 2000);
}
update();
