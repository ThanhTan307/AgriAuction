namespace AgriAuction.Infrastructure.Reporting;

internal static class ReportingSql
{
    internal const string Pending = """
        SELECT l.id, 'LotReview'::text kind, l.title, l.state, l.submitted_at occurred_at,
               NULL::text amount, NULL::text currency, NULL::text price_basis
        FROM lots l WHERE l.state='PendingReview' AND l.submitted_at>=@from AND l.submitted_at<@to
        UNION ALL
        SELECT r.id, 'DepositConfirmation', v.title, r.state, r.registered_at, NULL, NULL, NULL
        FROM auction_registrations r JOIN auctions a ON a.id=r.auction_id JOIN lot_versions v ON v.id=a.revision_id
        WHERE r.state='PendingDeposit' AND r.registered_at>=@from AND r.registered_at<@to
        UNION ALL
        SELECT c.id, CASE WHEN c.state='MediationPending' THEN 'MediationFollowUp' ELSE 'ComplaintReview' END,
               c.title, c.state, c.created_at, NULL, NULL, NULL
        FROM complaints c WHERE c.state IN ('Open','UnderReview','MediationPending') AND c.created_at>=@from AND c.created_at<@to
        """;
    internal static readonly string Metrics = """
        WITH pending AS (
        """ + Pending + """
        )
        SELECT 'Accounts'::text AS "Dimension", CASE WHEN is_enabled THEN 'Enabled' ELSE 'Disabled' END AS "State", role AS "Role", count(*) AS "Count"
        FROM users WHERE created_at>=@from AND created_at<@to GROUP BY role,is_enabled
        UNION ALL SELECT 'Lots',state,NULL,count(*) FROM lots WHERE created_at>=@from AND created_at<@to GROUP BY state
        UNION ALL SELECT 'Auctions',state,NULL,count(*) FROM auctions WHERE is_simulation AND created_at>=@from AND created_at<@to GROUP BY state
        UNION ALL SELECT 'Rooms',state,NULL,count(*) FROM simulation_rooms WHERE mode='Simulation' AND configured_at>=@from AND configured_at<@to GROUP BY state
        UNION ALL SELECT 'Results',CASE WHEN winner_id IS NULL THEN 'ClosedNoWinner' ELSE 'ClosedWithWinner' END,NULL,count(*)
          FROM simulation_results WHERE mode='Simulation' AND closed_at>=@from AND closed_at<@to GROUP BY winner_id IS NULL
        UNION ALL SELECT 'Bids','Accepted',NULL,count(*) FROM simulation_bids WHERE accepted_at>=@from AND accepted_at<@to
        UNION ALL SELECT 'Transactions',state,NULL,count(*) FROM post_auction_transactions WHERE mode='Simulation' AND created_at>=@from AND created_at<@to GROUP BY state
        UNION ALL SELECT 'Complaints',state,NULL,count(*) FROM complaints WHERE mode='Simulation' AND created_at>=@from AND created_at<@to GROUP BY state
        UNION ALL SELECT 'Pending',kind,NULL,count(*) FROM pending GROUP BY kind
        """;
    internal const string Values = """
        SELECT 'Winning'::text source, r.amount, s.currency, s.price_basis, r.closed_at occurred_at
        FROM simulation_results r JOIN simulation_rooms s ON s.id=r.room_id
        WHERE r.mode='Simulation' AND r.winner_id IS NOT NULL AND r.closed_at>=@from AND r.closed_at<@to
        UNION ALL
        SELECT 'Tracked',t.winning_price,t.currency,t.price_basis,t.created_at
        FROM post_auction_transactions t WHERE t.mode='Simulation' AND t.created_at>=@from AND t.created_at<@to
        """;
    internal static readonly string Totals = "WITH money_values AS (" + Values + """
        ) SELECT source AS "Source",currency AS "Currency",price_basis AS "PriceBasis",count(*) AS "Records",sum(amount)::text AS "Total"
        FROM money_values GROUP BY source,currency,price_basis ORDER BY source,currency,price_basis LIMIT 101
        """;
    internal static readonly string Daily = "WITH money_values AS (" + Values + """
        ) SELECT source AS "Source",currency AS "Currency",price_basis AS "PriceBasis",
          (occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS "Date",count(*) AS "Records",sum(amount)::text AS "Total"
        FROM money_values GROUP BY source,currency,price_basis,(occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date
        ORDER BY source,currency,price_basis,"Date"
        """;
    internal static string Detail(string dataset) => dataset switch
    {
        "lots" => "SELECT id,'Lots'::text kind,title,state,created_at occurred_at,NULL::text amount,NULL::text currency,NULL::text price_basis FROM lots WHERE created_at>=@from AND created_at<@to",
        "auctions" => "SELECT a.id,'Auctions'::text kind,v.title,a.state,a.created_at occurred_at,NULL::text amount,NULL::text currency,NULL::text price_basis FROM auctions a JOIN lot_versions v ON v.id=a.revision_id WHERE a.is_simulation AND a.created_at>=@from AND a.created_at<@to",
        "rooms" => "SELECT s.id,'Rooms'::text kind,v.title,s.state,s.configured_at occurred_at,NULL::text amount,NULL::text currency,NULL::text price_basis FROM simulation_rooms s JOIN auctions a ON a.id=s.auction_id JOIN lot_versions v ON v.id=a.revision_id WHERE s.mode='Simulation' AND s.configured_at>=@from AND s.configured_at<@to",
        "results" => "SELECT s.auction_id id,'Results'::text kind,v.title,CASE WHEN r.winner_id IS NULL THEN 'ClosedNoWinner' ELSE 'ClosedWithWinner' END state,r.closed_at occurred_at,r.amount::text amount,s.currency,s.price_basis FROM simulation_results r JOIN simulation_rooms s ON s.id=r.room_id JOIN auctions a ON a.id=s.auction_id JOIN lot_versions v ON v.id=a.revision_id WHERE r.mode='Simulation' AND r.closed_at>=@from AND r.closed_at<@to",
        "transactions" => "SELECT id,'Transactions'::text kind,title,state,created_at occurred_at,winning_price::text amount,currency,price_basis FROM post_auction_transactions WHERE mode='Simulation' AND created_at>=@from AND created_at<@to",
        "complaints" => "SELECT id,'Complaints'::text kind,title,state,created_at occurred_at,NULL::text amount,NULL::text currency,NULL::text price_basis FROM complaints WHERE mode='Simulation' AND created_at>=@from AND created_at<@to",
        "pending" => Pending,
        _ => throw new InvalidOperationException("Dataset must be validated before selecting SQL.")
    };
}
