// Hallow Keys - words for passphrases: common, short, easy to type English words (written for Hallow Keys).
using System.Linq;

namespace HallowKeys
{
    public static class Words
    {
        public static readonly string[] List = (
            "able acid acorn actor adapt adult agent agree ahead aisle alarm album alert alien alley allow alpha amber ample angle " +
            "ankle apple april apron arena argue armor arrow asset atlas attic audio avoid awake award badge bagel baker bamboo banjo " +
            "barn basil basin batch beach beard beast begin bench berry bike birch bison blade blank blast blaze blend blink bloom " +
            "board boat bonus boost booth boots bored brain brass brave bread brick bride brief bring brook broom brush bucket buddy " +
            "buggy bunny burst cabin cable cactus camel camp canal candy canoe canvas cargo carol carrot carve castle cedar chair chalk " +
            "charm chart cheek cheese cherry chess chest chief child chili chimp chord cider cinema civic claim clam clamp clay clerk " +
            "cliff climb cloak clock cloth cloud clown coach coast cobra cocoa comet comic coral corn couch cough count cover crab " +
            "craft crane crate crawl crayon cream creek crisp crow crown crumb crush crust cubic cupid curve cycle daisy dance " +
            "dash dawn decay decoy delta denim depth desk diary diner dingo disco ditch diver dizzy dock dodge dolphin donut dough " +
            "draft dragon drama drawn dream dress drift drill drink drive drone drum dusk dust eagle early earth easel eaten echo " +
            "eclipse edge eight elbow elder elect elite elk ember empty enjoy enter entry equal erase error essay ethic event exact " +
            "exile exit extra fable faint fairy faith false fancy fargo feast fence ferry fetch fever fiber field fifth fifty " +
            "final finch fire first fjord flag flame flash flask fleet flint float flock flood floor flour fluid flute focus foggy " +
            "forge forty forum fossil fox frame fresh frog frost fruit fudge fungi gamma gauge gecko genie ghost giant ginger glade " +
            "glass glide globe glove glow goat golden goose gorge grace grain grand grape graph grass gravy great green grill grove " +
            "guard guest guide guitar habit hammer happy harbor harp hasty hatch haven hazel heart heavy hedge hello heron hippo " +
            "hobby honey hoop horse hotel house humid humor husky icing igloo image index inlet ivory jacket jaguar jelly jewel " +
            "jolly judge juice jumbo jungle kayak kebab kettle kiosk kitten knack knee knife koala label ladder lake lamp lance " +
            "laser latch lava lawn layer leafy lemon level lilac limit linen lion liver llama lobby local lodge logic lotus lucky " +
            "lunar lunch lyric magic magma major mango maple marble march marsh mason medal melon mercy merry metal meteor " +
            "mild mimic minor mint mirror mixer mocha model mole money month moose morning motor mound mouse movie muffin " +
            "mural music nacho naval nectar nerve never night ninja noble noise north notch novel nurse nutmeg oasis ocean " +
            "olive omega onion opera orbit orchid organ otter outer owner oxide oyster paddle paint panda panel paper parade " +
            "parka party pasta patio peach pearl pecan pedal penny pepper perch piano pickle pilot pinch pine pixel pizza plaid " +
            "plane plank plant plaza plum poem polar pond poppy porch potato pouch power prism prize proud prune pulse puppy " +
            "quail quart queen quest quick quiet quilt quota rabbit radar radio rainy raisin rally ranch range rapid raven razor " +
            "reach ready rebel recap relax relic remix rhino rhyme ribbon ridge rifle rival river roast robin robot rocket rodeo " +
            "rose rover royal ruby rugby ruler rumba rural saddle safari salad salmon salsa sandy satin sauce scale scarf scene " +
            "scout screw seal sedan seed seven shade shark sheep shelf shell shine shiny shirt shore shrub siege silk silver " +
            "siren sketch skill skunk slate sleek slice slope smile smoke snack snail snake sneak solar sonic sound space spark " +
            "spice spider spike spoon sport spray spruce squad squid stack stage stair stamp stark steam steel stem stone storm " +
            "story stove straw stream stripe sugar sunny super surf swamp swan sweet swift syrup table taco talent tango tasty " +
            "teach tempo tennis thorn thumb thunder tiger timber toast token topaz torch total tower toxic track trail train " +
            "treat trend tribe trick trout truck trunk tulip tuna tunnel turbo turtle tutor twist ultra umbra uncle union unity " +
            "urban usher utter valid valley vapor vault velvet venom verse vest video vigor vinyl viola violet viper visit vista " +
            "vital vivid vocal voice voter wafer wagon waltz water wave wheat wheel whale whisk widow width wings wired wizard " +
            "wolf woman woody world worry wreck wrist yacht yearly yeast yellow yodel young zebra zesty zinc zippy zone").Split(' ').Where(w => w.Length > 0).Distinct().ToArray();
    }
}
