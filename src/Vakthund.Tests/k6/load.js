import http from 'k6/http';
import encoding from 'k6/encoding';
import { check, fail, sleep } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://localhost:18000';
const managementUrl = __ENV.MANAGEMENT_URL || 'http://localhost:18001';
const uiUrl = __ENV.UI_URL || 'http://localhost:18002';
const vus = Number(__ENV.K6_VUS || 200);
const duration = __ENV.K6_DURATION || '2m';

export const options = {
  scenarios: {
    hammer_proxy: {
      executor: 'ramping-vus',
      stages: [
        { duration: '15s', target: vus },
        { duration, target: vus },
        { duration: '15s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    http_req_duration: ['p(95)<1000'],
  },
};

export function setup() {
  waitFor(`${managementUrl}/`, 'proxy management port');
  waitFor(`${uiUrl}/`, 'ui');
}

export default function () {
  const token = buildJwt(__VU, __ITER);
  const headers = {
    Authorization: `Bearer ${token}`,
    'X-Load-Test': 'vakthund-k6',
    'X-VU': String(__VU),
    'X-Iteration': String(__ITER),
  };

  const payload = JSON.stringify({
    vu: __VU,
    iteration: __ITER,
    message: 'load-test-payload',
    values: [1, 2, 3, 4, 5],
  });

  const responses = http.batch([
    ['GET', `${baseUrl}/api/fast?vu=${__VU}&iteration=${__ITER}`, null, { headers }],
    ['GET', `${baseUrl}/api/data/${(__VU + __ITER) % 1000}`, null, { headers }],
    [
      'POST',
      `${baseUrl}/api/echo`,
      payload,
      {
        headers: {
          ...headers,
          'Content-Type': 'application/json',
        },
      },
    ],
  ]);

  check(responses[0], {
    'fast endpoint returns 200': response => response.status === 200,
  });

  check(responses[1], {
    'data endpoint returns 200': response => response.status === 200,
  });

  check(responses[2], {
    'echo endpoint returns 200': response => response.status === 200,
  });
}

function waitFor(url, name) {
  for (let attempt = 1; attempt <= 60; attempt += 1) {
    const response = http.get(url, { timeout: '2s' });
    if (response.status >= 200 && response.status < 500) {
      return;
    }

    sleep(1);
  }

  fail(`Timed out waiting for ${name} at ${url}`);
}

function buildJwt(vu, iteration) {
  const header = encodeBase64Url(JSON.stringify({ alg: 'none', typ: 'JWT' }));
  const payload = encodeBase64Url(JSON.stringify({
    sub: `load-user-${vu}`,
    scope: 'vakthund.load',
    iteration,
    exp: Math.floor(Date.now() / 1000) + 3600,
  }));

  return `${header}.${payload}.`;
}

function encodeBase64Url(value) {
  return encoding.b64encode(value, 'rawurl');
}
