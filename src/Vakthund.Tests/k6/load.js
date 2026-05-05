import http from 'k6/http';
import encoding from 'k6/encoding';
import { check, fail, sleep } from 'k6';
import { Trend } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:18000';
const managementUrl = __ENV.MANAGEMENT_URL || 'http://localhost:18001';
const uiUrl = __ENV.UI_URL || 'http://localhost:18002';
const vus = Number(__ENV.K6_VUS || 200);
const duration = __ENV.K6_DURATION || '2m';

const fastDuration = new Trend('vakthund_fast_duration', true);
const dataDuration = new Trend('vakthund_data_duration', true);
const echoDuration = new Trend('vakthund_echo_duration', true);

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
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
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

  fastDuration.add(responses[0].timings.duration);
  dataDuration.add(responses[1].timings.duration);
  echoDuration.add(responses[2].timings.duration);

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

export function handleSummary(data) {
  const summary = {
    generatedAt: new Date().toISOString(),
    metrics: pickMetrics(data.metrics, [
      'http_req_duration',
      'http_req_waiting',
      'http_req_blocked',
      'http_req_connecting',
      'http_req_tls_handshaking',
      'http_req_sending',
      'http_req_receiving',
      'http_req_failed',
      'http_reqs',
      'iterations',
      'vakthund_fast_duration',
      'vakthund_data_duration',
      'vakthund_echo_duration',
    ]),
  };

  return {
    stdout: textSummary(summary),
    '/results/summary.json': JSON.stringify(summary, null, 2),
  };
}

function pickMetrics(metrics, names) {
  const picked = {};
  for (const name of names) {
    if (metrics[name]) {
      picked[name] = metrics[name];
    }
  }

  return picked;
}

function textSummary(summary) {
  const lines = ['\nVakthund k6 timing summary'];

  for (const [name, metric] of Object.entries(summary.metrics)) {
    const values = metric.values || {};
    lines.push(`${name}: ${formatValues(values)}`);
  }

  lines.push(`\nJSON summary written to /results/summary.json\n`);
  return `${lines.join('\n')}\n`;
}

function formatValues(values) {
  const keys = ['count', 'rate', 'avg', 'min', 'med', 'p(90)', 'p(95)', 'max'];
  return keys
    .filter(key => values[key] !== undefined)
    .map(key => `${key}=${formatNumber(values[key])}`)
    .join(' ');
}

function formatNumber(value) {
  return typeof value === 'number' ? value.toFixed(2) : String(value);
}
