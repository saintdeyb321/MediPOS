using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediPOS.Infrastructure.Modules.Commissions.Persistence.Migrations;

public partial class FixCommissionRoundEven4 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // DiagnoseCommissionModel already preserves the public extensions. The new target model/snapshot
        // retain their explicit schema; this correction changes no extension or operational history.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION commission_round_even4(value numeric) RETURNS numeric
                LANGUAGE plpgsql IMMUTABLE STRICT AS $$
            DECLARE scaled numeric; whole numeric; fraction numeric;
            BEGIN
                IF value::text IN ('NaN', 'Infinity', '-Infinity') THEN
                    RAISE EXCEPTION 'A monetary amount must be finite.' USING ERRCODE = '22003';
                END IF;
                scaled := abs(value) * 10000;
                whole := trunc(scaled); fraction := scaled - whole;
                IF fraction > 0.5 OR (fraction = 0.5 AND mod(whole, 2) <> 0) THEN whole := whole + 1; END IF;
                IF whole > 999999999999999999 THEN
                    RAISE EXCEPTION 'Rounded amount exceeds numeric(18,4).' USING ERRCODE = '22003';
                END IF;
                RETURN (CASE WHEN value < 0 THEN -whole ELSE whole END) * 0.0001::numeric;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore the previous function body without dropping the function or its dependent triggers.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION commission_round_even4(value numeric) RETURNS numeric
                LANGUAGE plpgsql IMMUTABLE STRICT AS $$
            DECLARE scaled numeric := value * 10000; whole numeric; fraction numeric;
            BEGIN
                IF value < 0 THEN RAISE EXCEPTION 'Expected a nonnegative commission calculation.'; END IF;
                whole := trunc(scaled); fraction := scaled - whole;
                IF fraction > 0.5 OR (fraction = 0.5 AND mod(whole, 2) <> 0) THEN whole := whole + 1; END IF;
                RETURN whole / 10000;
            END $$;
            """);
    }
}
