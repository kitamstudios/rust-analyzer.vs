use std;
use shared::models::ModelA;
use shared2::models::ModelA2;
// use shared::systems::system_a:: { do_stuff_from_system_a }; // or (1)
// use shared::systems::system_a::*; // or this one (1)
use shared::systems::*; // or this one (2)
use shared2::systems::*; // or this one (2)

fn main() {
    let a = shared::A::new(10);
    let model_a = ModelA {
        value: 10,
    };

    let a2 = shared2::A2::new(10);
    let model_a2 = ModelA2 {
        value: 20,
    };

    println!("52 is equal to {}", a.get_value());
    println!("10 is equal to {}", model_a.value);
    println!("52 is equal to {}", a2.get_value());
    println!("10 is equal to {}", model_a2.value);
    // println!("must be 42 {}", do_stuff_from_system_a()); // (1)
    println!("42 is equal to {}", system_a::do_stuff_from_system_a()); // (2)
    println!("42 is equal to {}", system_a2::do_stuff_from_system_a2()); // (2)


    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}
