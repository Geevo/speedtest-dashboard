import test from 'node:test'
import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'
import { responseModels, endpointResponseModels } from '../src/lib/api-response-models.ts'

test('documented JSON models match backend field names, types and nullability', async () => {
  const endpoints = new URL('../../SpeedtestDashboard.Api/Endpoints/', import.meta.url)
  const files = (await readdir(endpoints)).filter(name => name.endsWith('.cs')).map(name => new URL(name, endpoints))
  files.push(new URL('../../SpeedtestDashboard.Core/Statistics/StatisticsContracts.cs', import.meta.url))
  const records = new Map()
  for (const file of files) {
    for (const [, name, body] of (await readFile(file, 'utf8')).matchAll(/public sealed record (\w+)\(\s*([^)]*)\)/g)) {
      records.set(name, body.split(',').map(field => field.trim().split(/\s+/)))
    }
  }
  const types = { string: 'string', Guid: 'string (uuid)', DateTimeOffset: 'string (date-time)', bool: 'boolean', int: 'integer (int32)', long: 'integer (int64)', decimal: 'number', JsonElement: 'any JSON value' }
  for (const [name, fields] of Object.entries(responseModels)) {
    const expected = records.get(name).map(([sourceType, sourceName]) => {
      const nullable = sourceType.endsWith('?')
      let type = sourceType.replace(/\?$/, '')
      const array = type.startsWith('IReadOnlyList<')
      if (array) type = type.slice(14, -1)
      return { name: sourceName[0].toLowerCase() + sourceName.slice(1), type: (types[type] ?? type) + (array ? '[]' : ''), nullable, ...(!types[type] ? { model: type } : {}) }
    })
    assert.deepEqual(fields, expected, name)
  }
  assert.equal(Object.keys(endpointResponseModels).length, 12)
  assert.equal(endpointResponseModels['/tests/{jobId}/events'], undefined)
  for (const model of Object.values(endpointResponseModels)) assert.ok(responseModels[model.replace(/\[\]$/, '')])
})
