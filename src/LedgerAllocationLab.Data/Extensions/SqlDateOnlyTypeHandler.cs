using System.Data;
using Dapper;

public class SqlDateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }

    public override DateOnly Parse(object value)
    {
        if (value is DateTime dateTime)
        {
            return DateOnly.FromDateTime(dateTime);
        }

        if (value is string str)
        {
            return DateOnly.Parse(str);
        }

        return DateOnly.FromDateTime(Convert.ToDateTime(value));
    }
}
