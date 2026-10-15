using MobaTypes;

// UserCharacterSelectSync 한 줄
public struct CharacterSelectEntry
{
    public int Uid;
    public PlayableCharacter Character;

    public CharacterSelectEntry(int uid, PlayableCharacter character)
    {
        Uid = uid;
        Character = character;
    }
}
