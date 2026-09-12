const readline = require('readline');
const { LoginSession, EAuthTokenPlatformType, EAuthSessionGuardType } = require('steam-session');

function ask(query) {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  return new Promise(resolve => rl.question(query, a => { rl.close(); resolve(a); }));
}

function askHidden(query) {
  return new Promise(resolve => {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
    // Register the prompt first (writes `query` via the real output), only
    // *then* swap in the muting override - otherwise the prompt itself gets
    // swallowed too and you end up typing blind into nothing.
    rl.question(query, value => { rl.close(); console.log(); resolve(value); });
    rl._writeToOutput = s => { if (!rl.stdoutMuted) rl.output.write(s); };
    rl.stdoutMuted = true;
  });
}

async function main() {
  const accountName = await ask('Steam-Benutzername: ');
  const password = await askHidden('Steam-Passwort: ');

  // MobileApp, not WebBrowser: GenerateAccessTokenForApp needs "mobile" in
  // the refresh token's aud claim, or it silently returns no access_token.
  const session = new LoginSession(EAuthTokenPlatformType.MobileApp);

  session.on('authenticated', () => {
    console.log('\nLogin erfolgreich. Refresh Token (in Steam__MarketRefreshToken eintragen):\n');
    console.log(session.refreshToken);
    process.exit(0);
  });

  session.on('error', err => {
    console.error('Login fehlgeschlagen:', err.message);
    process.exit(1);
  });

  const result = await session.startWithCredentials({ accountName, password });

  if (result.actionRequired) {
    const actions = result.validActions || [];
    // Steam often reports multiple valid options at once (e.g. app
    // confirmation AND a typed code). Prefer app confirmation when it's
    // available - it needs no code, we just wait for the 'authenticated'
    // event while the library polls in the background.
    const hasDeviceConfirmation = actions.some(a => a.type === EAuthSessionGuardType.DeviceConfirmation);
    const hasCodeAction = actions.some(a =>
      a.type === EAuthSessionGuardType.EmailCode || a.type === EAuthSessionGuardType.DeviceCode);

    if (hasDeviceConfirmation) {
      console.log('Bitte die Anmeldung jetzt in der Steam-Mobile-App bestätigen...');
    } else if (hasCodeAction) {
      const code = await ask('Steam-Guard-Code (aus E-Mail oder Authenticator): ');
      await session.submitSteamGuardCode(code);
    }
  }
}

main().catch(err => { console.error(err); process.exit(1); });
