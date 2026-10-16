// Generation guards reject work started by an older login/restore operation.
export function createSessionGuard(initialToken = null) {
  let token = initialToken, generation = 0;
  const snapshot = () => ({ token, generation });
  return {
    snapshot,
    replace(nextToken) { token = nextToken || null; generation++; return snapshot(); },
    isCurrent(expected) { return expected.generation === generation && expected.token === token; }
  };
}
export function requestToken(config) {
  const headers = config?.headers;
  const value = headers?.get?.('Authorization') || headers?.Authorization || headers?.authorization;
  return typeof value === 'string' && /^Bearer /i.test(value) ? value.slice(7) : null;
}
export function currentSessionExpired(error, token) {
  const path = (error?.config?.url || '').split('?')[0].replace(/\/$/,'');
  return Boolean(token && error?.response?.status === 401 && !/\/auth\/(login|google|register)$/i.test(path) && requestToken(error.config) === token);
}
export function createSerialQueue() {
  let tail = Promise.resolve();
  return action => {
    const result = tail.catch(() => {}).then(action);
    tail = result;
    return result;
  };
}
