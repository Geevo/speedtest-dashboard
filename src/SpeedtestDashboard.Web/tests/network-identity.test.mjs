import test from 'node:test'
import assert from 'node:assert/strict'
import { formatLocation } from '../src/lib/network-identity.ts'

const address = {
  address: '8.8.8.8',
  family: 'ipv4',
  asn: 'AS15169',
  asName: 'Google LLC',
  isp: null,
  countryCode: null,
  countryName: null,
  region: null,
  city: null,
  addressSource: 'ipify',
  metadataSource: 'IPConfig.io',
}

test('a placed address shows city, region and country', () => {
  assert.equal(
    formatLocation({ ...address, city: 'Watford', region: 'England', countryName: 'United Kingdom', countryCode: 'GB' }),
    'Watford, England - United Kingdom · GB',
  )
})

test('an address the database cannot place shows country alone', () => {
  assert.equal(
    formatLocation({ ...address, countryName: 'United States', countryCode: 'US' }),
    'United States · US',
  )
})

test('a partially placed address omits the missing part without stray separators', () => {
  assert.equal(formatLocation({ ...address, city: 'Watford', countryName: 'United Kingdom', countryCode: 'GB' }), 'Watford - United Kingdom · GB')
  assert.equal(formatLocation({ ...address, region: 'England' }), 'England')
  assert.equal(formatLocation({ ...address, countryName: 'United Kingdom' }), 'United Kingdom')
})

test('an address without any metadata renders nothing', () => {
  assert.equal(formatLocation(address), null)
})
