import { ChevronDown } from 'lucide-react'
import { endpointResponseModels, responseModels } from '../lib/api-response-models'

const endpoints = [
  ['GET', '/network', 'Read backend network identity', 'Returns public IPv4 and IPv6 identity.', '200 · Network identity'],
  ['GET', '/providers', 'List providers', 'Returns provider health and capabilities.', '200 · Provider array'],
  ['GET', '/providers/{providerId}', 'Read a provider', 'providerId: librespeed or ookla.', '200 · Provider · 404 not found'],
  ['GET', '/providers/{providerId}/servers', 'Search test servers', 'providerId: provider identifier. Optional query parameters: search (text), limit (integer).', '200 · Server array · 400 invalid query · 404 not found · 409 unavailable'],
  ['POST', '/tests', 'Queue a speed test', 'Optional Idempotency-Key header: up to 128 characters, retained for 24 hours. Reuse with the same request to retrieve the original job.', '202 · Created job · 400 invalid request · 404 not found · 409 conflict'],
  ['GET', '/tests/{jobId}', 'Read test status', 'jobId: the UUID returned when a test is queued.', '200 · Test job · 404 not found'],
  ['GET', '/tests/{jobId}/events', 'Stream test events', 'jobId: test UUID. Returns server-sent events (text/event-stream).', '200 · Event stream · 404 not found'],
  ['POST', '/tests/{jobId}/cancel', 'Cancel a test', 'jobId: test UUID. No request body required.', '202 · Test job · 404 not found · 409 cancellation unavailable'],
  ['GET', '/history', 'List test results', 'Returns a page of terminal results and a nextCursor for pagination.', '200 · Results page · 400 invalid query or cursor'],
  ['GET', '/history/{id}', 'Read a test result', 'id: numeric history record identifier.', '200 · Result detail · 404 not found'],
  ['GET', '/statistics', 'Read aggregate statistics', 'Optional range query: 24h, 7d (default), 30d, 90d, or all.', '200 · Aggregate statistics · 400 invalid query'],
  ['GET', '/schedules', 'List schedules', 'Returns configured schedules. Manage schedule changes in the dashboard.', '200 · Schedule array'],
  ['GET', '/schedules/{id}', 'Read a schedule', 'id: schedule UUID.', '200 · Schedule · 404 not found'],
] as const

export function ApiReference() {
  return (
    <section aria-labelledby="api-reference-heading" className="mt-9 border-t border-line pt-8">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 id="api-reference-heading" className="text-xl font-semibold tracking-tight">Available APIs</h3>
        <span className="text-xs font-semibold text-ink-muted">v1 · {endpoints.length} endpoints</span>
      </div>
      <p className="mt-2 text-sm leading-6 text-ink-muted">Expand an endpoint for request details and responses. Authenticate with your API key as a bearer token.</p>
      <pre className="mt-4 overflow-x-auto rounded-lg border border-line bg-paper p-4 text-xs leading-6"><code>{`curl '${window.location.origin}/api/v1/providers' \\\n  -H 'Authorization: Bearer <YOUR_API_KEY>'`}</code></pre>
      <p className="mt-3 text-xs leading-5 text-ink-muted">Responses use JSON unless indicated. All endpoints can return 401 for an invalid or revoked key and 429 when rate limited.</p>
      <div className="mt-6 space-y-2">
        {endpoints.map(([method, path, title, detail, response]) => (
          <details key={`${method} ${path}`} className="group overflow-hidden rounded-lg border border-line bg-paper open:border-ink-muted/60">
            <summary className="flex min-h-14 list-none flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 hover:bg-signal-soft/40 [&::-webkit-details-marker]:hidden">
              <span className={`w-14 shrink-0 rounded px-2 py-1 text-center text-xs font-bold ${method === 'GET' ? 'bg-signal-soft text-signal' : 'bg-api-post-soft text-api-post'}`}>{method}</span>
              <code className="min-w-0 flex-1 break-all text-xs sm:flex-none sm:text-sm">/api/v1{path}</code>
              <span className="order-last w-full text-sm text-ink-muted sm:order-none sm:w-auto">{title}</span>
              <ChevronDown aria-hidden="true" className="ml-auto size-4 shrink-0 text-ink-muted transition-transform group-open:rotate-180" />
            </summary>
            <div className="space-y-4 border-t border-line px-4 py-5 text-sm leading-6">
              <p>{detail}</p>
              {method === 'POST' && path === '/tests' && <div><p className="mb-2 font-semibold">Request body · application/json</p><pre className="overflow-x-auto rounded bg-canvas p-3 text-xs"><code>{'{ "providerId": "librespeed", "serverId": null }'}</code></pre></div>}
              <div><p className="font-semibold">Responses</p><p className="text-ink-muted">{response}</p></div>
              {endpointResponseModels[path] && (
                <div className="min-w-0">
                  <p className="mb-2 font-semibold">Response model · application/json</p>
                  <p className="mb-3 text-xs text-ink-muted">Field types describe the JSON response. Nullable fields can contain null. Expand object fields to inspect their properties.</p>
                  <ResponseModel name={endpointResponseModels[path]} />
                </div>
              )}
            </div>
          </details>
        ))}
      </div>
    </section>
  )
}

function ResponseModel({ name }: { name: string }) {
  const array = name.endsWith('[]')
  const model = array ? name.slice(0, -2) : name
  return (
    <div className="min-w-0 rounded-lg border border-line">
      <div className="break-words border-b border-line bg-canvas px-3 py-2 font-mono text-xs">{name}{array && <span className="ml-2 font-sans text-ink-muted">Array of objects</span>}</div>
      <dl className="divide-y divide-line px-3">
        {responseModels[model].map((field) => (
          <div key={field.name} className="py-3">
            <dt className="break-words font-mono text-xs font-semibold">{field.name}</dt>
            <dd className="mt-1 min-w-0 text-xs text-ink-muted">
              <span className="break-words">{field.type}{field.nullable ? ' · nullable' : ''}</span>
              {field.model && (
                <details className="mt-2 text-ink">
                  <summary className="w-fit py-1 font-semibold">Properties</summary>
                  <div className="mt-2"><ResponseModel name={field.model + (field.type.endsWith('[]') ? '[]' : '')} /></div>
                </details>
              )}
            </dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
