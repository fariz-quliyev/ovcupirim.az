import { execFile } from 'node:child_process'
import { promisify } from 'node:util'

const run = promisify(execFile)

/**
 * Removes what previous end-to-end runs left behind.
 *
 * Without this the suite is not repeatable: the moderation queue fills with listings from every
 * earlier run, "the first row" stops meaning anything, and a test that passed yesterday fails today
 * for reasons that have nothing to do with the code. Everything these tests create is marked, and
 * only marked rows are removed — real development data is never touched.
 *
 * Deletion order follows the foreign keys: a listing cannot outlive its media, and a user cannot be
 * removed while a listing still points at them.
 */
const MARKER = '[e2e]'

/** Plain statements rather than a DO block: easier to read, and easier to debug when one fails. */
const CLEANUP = [
  `DELETE FROM "StoreFollows" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')
      OR "StoreId" IN (SELECT "Id" FROM "Stores" WHERE "OwnerUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  `DELETE FROM "Favorites" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')
      OR "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  `DELETE FROM "Reports" WHERE "ReporterUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')
      OR "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  `DELETE FROM "ModerationActions" WHERE "ModeratorUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')
      OR "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  // Payment/promotion rows sit between a user and their listings in the foreign-key graph — both
  // PaymentOrders and Promotions reference Listings with ON DELETE RESTRICT (financial and audit
  // records must never silently vanish when a listing does), so they have to go before Listings,
  // in their own dependency order: transactions before orders, and promotions before either.
  `DELETE FROM "PaymentTransactions" WHERE "PaymentOrderId" IN (
      SELECT "Id" FROM "PaymentOrders" WHERE "SellerUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')
        OR "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')))`,
  `DELETE FROM "Promotions" WHERE "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))
      OR "PaymentOrderId" IN (SELECT "Id" FROM "PaymentOrders" WHERE "SellerUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  `DELETE FROM "PaymentOrders" WHERE "SellerUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')
      OR "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  // PromotionPackages carry no user reference of their own — the E2E suite marks them by code
  // instead (see 09-promotions.spec.ts) — and are only safe to remove once nothing above still
  // points at them.
  `DELETE FROM "PromotionPackages" WHERE "Code" LIKE 'e2e-bump-%'`,
  `DELETE FROM "ListingMedia" WHERE "ListingId" IN (SELECT "Id" FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'))`,
  `DELETE FROM "Listings" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')`,
  `DELETE FROM "ListingQuotas" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')`,
  `DELETE FROM "Stores" WHERE "OwnerUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')`,
  `DELETE FROM "AuditLogs" WHERE "ActorUserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')`,
  `DELETE FROM "RefreshTokens" WHERE "UserId" IN (SELECT "Id" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')`,
  // OtpCodes are keyed by phone number rather than by user.
  `DELETE FROM "OtpCodes" WHERE "PhoneNumber" IN (SELECT "PhoneNumber" FROM "Users" WHERE "FullName" LIKE '%${MARKER}%')`,
  `DELETE FROM "Users" WHERE "FullName" LIKE '%${MARKER}%'`,
]

/**
 * Runs one statement, through whichever psql is available.
 *
 * Locally that is the development container; on a CI runner the database is a service container
 * and psql is on the PATH, so the same statements go through a connection string instead.
 */
async function execute(statement: string): Promise<void> {
  const url =
    process.env['OVCUPIRIM_E2E_DB'] ??
    'postgresql://ovcuprim:ovcuprim_dev@localhost:5433/ovcuprim'

  if (process.env['CI']) {
    await run('psql', [url, '-v', 'ON_ERROR_STOP=1', '-c', statement])

    return
  }

  await run('docker', [
    'exec',
    'ovcuprim-postgres',
    'psql',
    '-U',
    'ovcuprim',
    '-d',
    'ovcuprim',
    '-v',
    'ON_ERROR_STOP=1',
    '-c',
    statement,
  ])
}

export default async function globalSetup(): Promise<void> {
  try {
    for (const statement of CLEANUP) {
      await execute(statement)
    }
  } catch (error) {
    // A missing container is a setup problem worth failing on: without cleanup the run is not
    // repeatable, and a green result would be meaningless.
    throw new Error(
      `Could not clear previous end-to-end data. Is the development database running?\n${String(error)}`,
    )
  }
}

export { MARKER }
