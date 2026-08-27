using System.Data;
using Dapper;
using NpgsqlTypes;

namespace HousingPolicy.Api.Json;

// Dapper + Npgsql don't auto-handle DateOnly as a parameter; this teaches
// Dapper to send it as a PG `date` (used for bills.latest_action_date).
// Mirrors the house handler in mypfsa/pfsa-api.

internal sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d  => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _           => DateOnly.FromDateTime(Convert.ToDateTime(value)),
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.Value = value;
        if (parameter is Npgsql.NpgsqlParameter np)
            np.NpgsqlDbType = NpgsqlDbType.Date;
    }
}

internal sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public override DateOnly? Parse(object value) => value switch
    {
        null        => null,
        DateOnly d  => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _           => DateOnly.FromDateTime(Convert.ToDateTime(value)),
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
    {
        parameter.Value = (object?)value ?? DBNull.Value;
        if (parameter is Npgsql.NpgsqlParameter np)
            np.NpgsqlDbType = NpgsqlDbType.Date;
    }
}
