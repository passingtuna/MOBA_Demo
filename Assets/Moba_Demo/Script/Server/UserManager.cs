using MobaTypes;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;

// 서버 메모리상 유저 (로그인 시 SP 결과로 채움, Dirty면 UpdateUserData SP)
public class UserManager
{
    public static UserManager Instance;
    public ConcurrentDictionary<int, UserData> dicUserData = new();

    public UserManager()
    {
        Instance = this;
    }

    public void SaveAllDirtyUsers()
    {
        foreach (var kvp in dicUserData)
        {
            if (kvp.Value.DirtyFlag)
            {
                UpdateDirtyUserData(kvp.Key);
            }
        }
    }

    public void LogOutUser(int userid)
    {
        UpdateDirtyUserData(userid);
        dicUserData.TryRemove(userid, out _);
    }

    public void UpdateDirtyUserData(int Userid)
    {
        UserData TargetData;
        if (dicUserData.TryGetValue(Userid, out TargetData))
        {
            if (TargetData.DirtyFlag)
            {
                DBManager.SQLData data = new DBManager.SQLData();
                data.sqlQuery = "UpdateUserData";
                data.AddInput("@UserId", SqlDbType.Int, Userid);
                data.AddInput("@Money", SqlDbType.Int, TargetData.GetMoney());
                data.AddInput("@MMR", SqlDbType.Int, TargetData.GetMMR());
                data.AddInput("@NickName", SqlDbType.VarChar, TargetData.GetUserNickName(), 50);
                data.AddOutput("@Result", SqlDbType.Int);
                DBManager.Instance.SendDBSQL(data);
                TargetData.MarkClean();
            }

        }
    }

    public UserData GetUserData(int userid)
    {
        UserData targetData = null;
        if (dicUserData.TryGetValue(userid, out targetData))
        {
            return targetData;
        }
        return targetData;
    }
}
