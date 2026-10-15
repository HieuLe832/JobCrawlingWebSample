async function request(path, options = {}) {
  const res = await fetch(`/api${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  const body = await res.json().catch(() => null)
  if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`)
  return body
}

const qs = (params) =>
  new URLSearchParams(Object.entries(params).filter(([, v]) => v !== '' && v != null)).toString()

export const api = {
  startCrawl: (sources, maxPages) =>
    request('/crawl', { method: 'POST', body: JSON.stringify({ sources, maxPages }) }),
  crawlStatus: () => request('/crawl/status'),
  cancelCrawl: () => request('/crawl/cancel', { method: 'POST' }),
  jobs: (filters) => request(`/jobs?${qs(filters)}`),
  stats: (includeNonIt) => request(`/jobs/stats?${qs({ includeNonIt })}`),
  reclassify: () => request('/jobs/reclassify', { method: 'POST' }),
}
