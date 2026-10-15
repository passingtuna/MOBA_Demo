using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
//using System.Security.Cryptography;

// SP 호출은 SendQueue → Moba_Server.DBWorkerLoop에서 순차 실행 (메인 스레드 막지 않음)
public class DBManager
{
    public static DBManager Instance;

    public readonly ConcurrentQueue<SQLData> SendQueue = new();
    string ConnectionString;
    public class SqlParam
    {
        public string Name;
        public SqlDbType Type;
        public object Value;
        public int Size;
        public ParameterDirection Direction;
    }

    public static T GetValue<T>(Dictionary<string, object> dict, string key)
    {
        if (dict == null) return default;

        if (dict.TryGetValue(key, out var value))
        {
            if (value == null || value is DBNull)
                return default;

            return (T)Convert.ChangeType(value, typeof(T));
        }

        return default;
    }

    public class SQLData
    {
        public String sqlQuery = null;
        public List<SqlParam> Parameters = new();
        public TaskCompletionSource<Dictionary<string, object>> Tcs;

        public void AddInput(string name, SqlDbType type, object value, int size = 0)
        {
            Parameters.Add(new SqlParam
            {
                Name = name,
                Type = type,
                Value = value,
                Size = size,
                Direction = ParameterDirection.Input
            });
        }

        public void AddOutput(string name, SqlDbType type, int size = 0)
        {
            Parameters.Add(new SqlParam
            {
                Name = name,
                Type = type,
                Size = size,
                Direction = ParameterDirection.Output
            });
        }
    }

    public DBManager()
    {
        Instance = this;
        ConnectionString = "Server=localhost;Database=Game_DataBase;Integrated Security=True;";
    }

    private void AddParameters(SqlCommand cmd, SQLData data)
    {
        foreach (var param in data.Parameters)
        {
            SqlParameter sqlparam;

            if (param.Type == SqlDbType.VarChar || param.Type == SqlDbType.NVarChar)
                sqlparam = new SqlParameter(param.Name, param.Type, param.Size);
            else
                sqlparam = new SqlParameter(param.Name, param.Type);

            sqlparam.Direction = param.Direction;

            if (param.Direction == ParameterDirection.Input)
                sqlparam.Value = param.Value;

            cmd.Parameters.Add(sqlparam);
        }

    }

    private Dictionary<string, object> ReadOutputParameters(SqlCommand cmd, SQLData data)
    {
        var outputValues = new Dictionary<string, object>();

        if (data.Parameters != null)
        {
            foreach (var paramInfo in data.Parameters)
            {
                if (paramInfo.Direction == ParameterDirection.Output ||
                    paramInfo.Direction == ParameterDirection.InputOutput)
                {
                    var value = cmd.Parameters[paramInfo.Name].Value;

                    outputValues[paramInfo.Name] = value == DBNull.Value ? null : value;
                }
            }
        }

        return outputValues;
    }

    public void SendDBSQL(SQLData data)
    {
        SendQueue.Enqueue(data);
    }

    public async Task<Dictionary<string, object>> ExecuteStoredProcedureAsync(SQLData data)
    {
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        using var cmd = new SqlCommand(data.sqlQuery, conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        AddParameters(cmd, data);
        try
        {
            await cmd.ExecuteNonQueryAsync();
            return ReadOutputParameters(cmd, data);
        }
        catch (Exception ex)
        {
            ServerLog.Error($"프로시저 {data.sqlQuery} 실행 실패: {ex}");
            return null;
        }
    }

    public async Task FlushAllPending()
    {
        while (SendQueue.TryDequeue(out var data))
        {
            try
            {
                var result = await ExecuteStoredProcedureAsync(data);
                data.Tcs?.SetResult(result);
            }
            catch (Exception e)
            {
                data.Tcs?.SetException(e);
                ServerLog.Error($"DB Worker Error: {e}");
            }
        }
    }
}

